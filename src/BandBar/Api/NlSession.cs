using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NetLimiter.Service;

namespace BandBar.Api
{
    /// <summary>
    /// The only class that uses NetLimiter API types (docs/DESIGN.md). It is first touched from inside a try/catch in
    /// <see cref="NlMonitor"/>, after <see cref="NlApiLoader"/> is installed, so a missing or refused API becomes a
    /// status instead of a crash.
    /// </summary>
    internal sealed class NlSession : INlSession
    {
        private readonly NLClient client;
        private readonly NodeLoader loader;
        private readonly uint zoneInternalId;
        private readonly Action onDisconnected;

        private NlSession(NLClient client, NodeLoader loader, Filter zone, Action onDisconnected)
        {
            this.client = client;
            this.loader = loader;
            this.onDisconnected = onDisconnected;
            zoneInternalId = zone.InternalId;
            ZoneId = zone.Id;
            ZoneName = zone.Name;
            ServiceVersion = client.ServiceVersion;
            client.StateChanged += OnStateChanged;
        }

        public string ZoneId { get; }
        public string ZoneName { get; }
        public string ServiceVersion { get; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static INlSession Open(string zoneFilterId, Action onDisconnected)
        {
            var client = new NLClient();
            try
            {
                // No host, port or credentials: the local service over its named pipe. Never remote, never TCP.
                client.Connect();
                if (client.State != NLServiceState.Connected)
                    throw new InvalidOperationException("NetLimiter service connection was not established.", client.ConnectError);

                Filter zone = FindZone(client, zoneFilterId);
                NodeLoader loader = client.CreateNodeLoader();
                loader.Filters.SelectAll(); // filter nodes only; applications and connections are never requested
                return new NlSession(client, loader, zone, onDisconnected);
            }
            catch
            {
                Close(client);
                throw;
            }
        }

        public Rate Read()
        {
            loader.Load();
            foreach (FilterNode node in loader.Filters.Nodes)
            {
                if (node.FilterId == zoneInternalId)
                {
                    INLFlowValue<uint> rate = node.TransferRate;
                    return new Rate(rate.In, rate.Out, zoneFound: true);
                }
            }
            return new Rate(0, 0, zoneFound: false);
        }

        // Read only (docs/DESIGN.md §3.4): the feature switches are only looked at, never set.
        public NlDetails ReadDetails()
        {
            Filter internet = client.GetInternetZone();
            var zones = new List<ZoneInfo>();
            foreach (Filter filter in client.Filters)
            {
                if (filter.Type == FilterType.Zone)
                    zones.Add(new ZoneInfo(filter.Id, filter.Name, internet != null && filter.Id == internet.Id));
            }
            // The Internet zone first, then the others by name.
            zones.Sort((a, b) => a.IsInternet != b.IsInternet ? (a.IsInternet ? -1 : 1)
                                 : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            return new NlDetails(zones, client.IsLimiterEnabled, client.IsFwEnabled, client.IsPriorityEnabled);
        }

        // Only counts. Never ReplyFirewallRequest, never SubscribeAsFwReqeustHandler, no FirewallRequestChange
        // subscription: the service must never wait for BandBar to answer (docs/DESIGN.md §3.4).
        public int CountBlockerRequests() => client.FirewallRequests?.Count ?? 0;

        public void Dispose()
        {
            client.StateChanged -= OnStateChanged;
            try
            {
                loader.Dispose();
            }
            catch (Exception ex)
            {
                Log.Debug("Disposing the node loader failed", ex);
            }
            Close(client);
        }

        private static Filter FindZone(NLClient client, string zoneFilterId)
        {
            if (!string.IsNullOrEmpty(zoneFilterId))
            {
                foreach (Filter filter in client.Filters)
                {
                    if (filter.Id == zoneFilterId)
                        return filter;
                }
                Log.Warn("Configured zone not found; using the Internet zone");
            }
            return client.GetInternetZone() ?? throw new InvalidOperationException("NetLimiter reported no Internet zone.");
        }

        // Raised on a WCF thread: only signal, never throw.
        private void OnStateChanged(object sender, EventArgs e)
        {
            try
            {
                NLServiceState state = client.State;
                if (state == NLServiceState.Disconnecting || state == NLServiceState.Disconnected)
                    onDisconnected();
            }
            catch (Exception ex)
            {
                Log.Debug("StateChanged handler failed", ex);
            }
        }

        private static void Close(NLClient client)
        {
            try
            {
                client.Close();
            }
            catch (Exception ex)
            {
                Log.Debug("Closing the NetLimiter client failed", ex);
            }
            try
            {
                client.Dispose();
            }
            catch (Exception ex)
            {
                Log.Debug("Disposing the NetLimiter client failed", ex);
            }
        }
    }
}
