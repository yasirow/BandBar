using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;

namespace BandBar.Api
{
    /// <summary>
    /// Keeps the connection to the local NetLimiter service on a background thread and publishes one
    /// <see cref="Reading"/> per second while not paused (docs/DESIGN.md). Uses no NetLimiter types itself.
    /// </summary>
    internal sealed class NlMonitor : IDisposable
    {
        private const int ReadIntervalMs = 1000;
        private const int ReadTimeoutMs = 2000;
        private static readonly int[] RetryDelaysMs = { 2000, 4000, 8000, 16000, 30000 };

        private readonly Action<Reading> publish;
        private readonly Thread thread;
        private readonly ManualResetEvent stopRequested = new ManualResetEvent(false);
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly WaitHandle[] waitHandles;

        private volatile string zoneFilterId;
        private volatile bool zoneChanged;
        private volatile bool paused;
        private volatile bool disconnected;
        private INlSession session;
        private int failedAttempts;
        private bool zoneMissingLogged;
        private LinkStatus lastStatus = LinkStatus.Connecting;
        private string lastDetail;

        /// <param name="publish">Called on the monitor thread.</param>
        public NlMonitor(string zoneFilterId, Action<Reading> publish)
        {
            this.zoneFilterId = zoneFilterId;
            this.publish = publish;
            waitHandles = new WaitHandle[] { stopRequested, wake };
            thread = new Thread(Run) { IsBackground = true, Name = "NetLimiter monitor" };
        }

        public void Start() => thread.Start();

        /// <summary>Shows another zone from the next reading on (reconnects once); null is the Internet zone.</summary>
        public void SetZone(string filterId)
        {
            zoneFilterId = filterId;
            zoneChanged = true;
            wake.Set();
        }

        /// <summary>While paused nothing is read; the idle connection is kept.</summary>
        public void SetPaused(bool value)
        {
            paused = value;
            if (!value)
                wake.Set();
        }

        public void Dispose()
        {
            stopRequested.Set();
            if (thread.IsAlive && !thread.Join(1500))
                Log.Warn("Monitor thread did not stop in time");
        }

        private void Run()
        {
            try
            {
                while (!stopRequested.WaitOne(0))
                {
                    if (paused)
                    {
                        WaitHandle.WaitAny(waitHandles);
                        continue;
                    }

                    if (zoneChanged)
                    {
                        // The zone is chosen when the session opens: start a new one.
                        zoneChanged = false;
                        zoneMissingLogged = false;
                        CloseSession();
                        failedAttempts = 0;
                    }

                    if (session == null && !TryOpen())
                    {
                        WaitHandle.WaitAny(waitHandles, RetryDelaysMs[Math.Min(failedAttempts, RetryDelaysMs.Length) - 1]);
                        continue;
                    }

                    // After a failed read the session is gone; the next pass reconnects at once.
                    if (TryRead())
                        WaitHandle.WaitAny(waitHandles, ReadIntervalMs);
                }
            }
            catch (Exception ex)
            {
                // Last line of defence: this thread must never take the process down.
                Log.Error("Monitor thread stopped unexpectedly", ex);
                Publish(new Reading(LinkStatus.NoService, detail: ex.GetType().Name), null);
            }
            finally
            {
                CloseSession();
            }
        }

        private bool TryOpen()
        {
            if (failedAttempts == 0)
                Publish(Reading.Connecting, null);
            try
            {
                disconnected = false;
                session = OpenSession();
                failedAttempts = 0;
                return true;
            }
            catch (Exception ex)
            {
                failedAttempts++;
                Exception cause = Unwrap(ex);
                Publish(new Reading(Classify(cause), detail: cause.GetType().Name), cause);
                return false;
            }
        }

        // A separate method so that a NetLimiter API that cannot be loaded fails here, inside TryOpen's try block.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private INlSession OpenSession() => NlSession.Open(zoneFilterId, OnDisconnected);

        private void OnDisconnected()
        {
            disconnected = true;
            wake.Set();
        }

        private bool TryRead()
        {
            INlSession current = session;
            try
            {
                if (disconnected)
                    throw new CommunicationException("NetLimiter service closed the connection.");

                Task<Rate> read = Task.Run(() => current.Read());
                if (!read.Wait(ReadTimeoutMs))
                {
                    read.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    throw new TimeoutException("NetLimiter service did not answer within 2 s.");
                }

                Rate rate = read.Result;
                if (!rate.ZoneFound && !zoneMissingLogged)
                {
                    zoneMissingLogged = true;
                    Log.Warn("Zone node missing from the service data; showing 0");
                }
                Publish(new Reading(LinkStatus.Connected, rate.Down, rate.Up, current.ZoneName, current.ServiceVersion), null);
                return true;
            }
            catch (Exception ex)
            {
                CloseSession();
                Exception cause = Unwrap(ex);
                LinkStatus status = Classify(cause);
                Publish(new Reading(status, detail: cause.GetType().Name), cause);
                return false;
            }
        }

        private void Publish(Reading reading, Exception cause)
        {
            if (reading.Status != lastStatus || reading.Detail != lastDetail)
            {
                if (reading.Status == LinkStatus.Connected)
                    Log.Info("NetLimiter link: connected (service " + reading.ServiceVersion + ")");
                else if (reading.Status != LinkStatus.Connecting)
                    Log.Warn("NetLimiter link: " + reading.Status, cause);
                lastStatus = reading.Status;
                lastDetail = reading.Detail;
            }

            try
            {
                publish(reading);
            }
            catch (Exception ex)
            {
                Log.Warn("Publishing a reading failed", ex);
            }
        }

        private void CloseSession()
        {
            INlSession current = session;
            session = null;
            if (current == null)
                return;
            try
            {
                current.Dispose();
            }
            catch (Exception ex)
            {
                Log.Debug("Closing the NetLimiter session failed", ex);
            }
        }

        private static Exception Unwrap(Exception ex)
        {
            while (true)
            {
                if (ex is AggregateException aggregate && aggregate.InnerExceptions.Count == 1)
                    ex = aggregate.InnerExceptions[0];
                else if ((ex is TypeInitializationException || ex is System.Reflection.TargetInvocationException) && ex.InnerException != null)
                    ex = ex.InnerException;
                else
                    return ex;
            }
        }

        // Matches NetLimiter exceptions by name so that classifying needs no NetLimiter type.
        private static LinkStatus Classify(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                string name = e.GetType().Name;
                if (name == "AccessDeniedException" || name == "AdminRequiredException" || name == "AccessLossException"
                    || e is UnauthorizedAccessException || e is System.ServiceModel.Security.SecurityAccessDeniedException)
                    return LinkStatus.AccessDenied;

                if (e is FileNotFoundException || e is FileLoadException)
                {
                    switch (NlApiLoader.LastFailure)
                    {
                        case ApiLoadFailure.NotInstalled: return LinkStatus.NotInstalled;
                        case ApiLoadFailure.Untrusted: return LinkStatus.ApiUntrusted;
                        default: return LinkStatus.ApiIncompatible;
                    }
                }

                if (e is TypeLoadException || e is MissingMemberException || e is BadImageFormatException)
                    return LinkStatus.ApiIncompatible;
            }
            return LinkStatus.NoService;
        }
    }
}
