using System;
using Microsoft.AspNetCore.Authentication;

namespace JsonApiDotNetCoreExampleTests
{
    internal sealed class FrozenSystemClock : ISystemClock
    {
        private static readonly DateTimeOffset _defaultTime =
            new DateTimeOffset(new DateTime(2000, 1, 1, 1, 1, 1), TimeSpan.FromHours(1));

        public DateTimeOffset UtcNow { get; set; } = _defaultTime;
    }
}
