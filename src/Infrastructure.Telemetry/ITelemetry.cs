using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Telemetry
{
    public interface ITelemetry
    {
        ActivitySource ActivitySource { get; }

    }
}
