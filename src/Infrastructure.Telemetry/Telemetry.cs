using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Telemetry
{
    public class Telemetry : ITelemetry
    {
        public ActivitySource ActivitySource { get; }

        public Telemetry(string serviceName)
        {
            ActivitySource = new ActivitySource(serviceName);
        }

        
    }
}
