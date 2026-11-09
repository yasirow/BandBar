namespace BandBar.Api
{
    internal enum LinkStatus
    {
        Connecting,
        Connected,
        NoService,
        AccessDenied,
        NotInstalled,
        ApiUntrusted,
        ApiIncompatible,
    }

    /// <summary>One snapshot published by <see cref="NlMonitor"/> to the UI. Immutable.</summary>
    internal sealed class Reading
    {
        public static readonly Reading Connecting = new Reading(LinkStatus.Connecting);

        public Reading(LinkStatus status, uint down = 0, uint up = 0, string zoneName = null,
                       string serviceVersion = null, string detail = null, string zoneId = null,
                       NlDetails details = null, int blockerRequests = 0)
        {
            Status = status;
            Down = down;
            Up = up;
            ZoneName = zoneName;
            ServiceVersion = serviceVersion;
            Detail = detail;
            ZoneId = zoneId;
            Details = details;
            BlockerRequests = blockerRequests;
        }

        /// <summary>ID of the zone shown.</summary>
        public string ZoneId { get; }

        /// <summary>Zones and feature states for the menu; null when not (yet) known.</summary>
        public NlDetails Details { get; }

        /// <summary>Connections waiting for the Blocker's approval.</summary>
        public int BlockerRequests { get; }

        public LinkStatus Status { get; }

        /// <summary>Bytes per second.</summary>
        public uint Down { get; }

        /// <summary>Bytes per second.</summary>
        public uint Up { get; }

        public string ZoneName { get; }
        public string ServiceVersion { get; }

        /// <summary>Exception type name behind a failure; never a message (messages can contain paths).</summary>
        public string Detail { get; }
    }

    /// <summary>Rate of the selected zone, bytes per second.</summary>
    internal readonly struct Rate
    {
        public Rate(uint down, uint up, bool zoneFound)
        {
            Down = down;
            Up = up;
            ZoneFound = zoneFound;
        }

        public uint Down { get; }
        public uint Up { get; }
        public bool ZoneFound { get; }
    }

    /// <summary>A NetLimiter zone, for the Zone menu.</summary>
    internal sealed class ZoneInfo
    {
        public ZoneInfo(string id, string name, bool isInternet)
        {
            Id = id;
            Name = name;
            IsInternet = isInternet;
        }

        public string Id { get; }
        public string Name { get; }
        public bool IsInternet { get; }
    }

    /// <summary>What the menu shows besides the speed (docs/DESIGN.md §3.4). Read only; immutable.</summary>
    internal sealed class NlDetails
    {
        public NlDetails(System.Collections.Generic.IReadOnlyList<ZoneInfo> zones, bool limiterOn, bool blockerOn, bool prioritiesOn)
        {
            Zones = zones;
            LimiterOn = limiterOn;
            BlockerOn = blockerOn;
            PrioritiesOn = prioritiesOn;
        }

        public System.Collections.Generic.IReadOnlyList<ZoneInfo> Zones { get; }
        public bool LimiterOn { get; }
        public bool BlockerOn { get; }
        public bool PrioritiesOn { get; }
    }

    /// <summary>
    /// A live connection to the local NetLimiter service. Declared without NetLimiter types so that code holding
    /// one can be compiled by the JIT even when the API cannot be loaded. Every member only reads.
    /// </summary>
    internal interface INlSession : System.IDisposable
    {
        string ZoneId { get; }
        string ZoneName { get; }
        string ServiceVersion { get; }
        Rate Read();

        /// <summary>Zones and the Limiter/Blocker/Priorities states.</summary>
        NlDetails ReadDetails();

        /// <summary>How many connections NetLimiter's Blocker holds for approval. Never answers them.</summary>
        int CountBlockerRequests();
    }
}
