﻿using Newtonsoft.Json;
using System;

namespace FM.LiveSwitch.Hammer
{
    class ScanTestScenarioResult
    {
        public ScanTestScenario Scenario { get; private set; }

        public ScanTestState State { get; private set; }

        [JsonIgnore]
        public TimeSpan? CertificateValidFor { get; private set; }

        public int? CertificateDays
        {
            get
            {
                var certificateValidFor = CertificateValidFor;
                if (certificateValidFor == null)
                {
                    return null;
                }
                return (int)Math.Max(0, certificateValidFor.Value.TotalDays);
            }
        }

        public Exception Exception { get; private set; }

        public string Reason { get; private set; }

        private ScanTestScenarioResult() { }

        public static ScanTestScenarioResult Unknown(ScanTestScenario scenario)
        {
            return new ScanTestScenarioResult
            {
                Scenario = scenario,
                State = ScanTestState.Unknown
            };
        }

        public static ScanTestScenarioResult Pass(ScanTestScenario scenario, TimeSpan? certificateValidFor)
        {
            return new ScanTestScenarioResult
            {
                Scenario = scenario,
                State = ScanTestState.Pass,
                CertificateValidFor = certificateValidFor
            };
        }

        public static ScanTestScenarioResult Fail(ScanTestScenario scenario, Exception exception)
        {
            return new ScanTestScenarioResult
            {
                Scenario = scenario,
                State = ScanTestState.Fail,
                Reason = exception.Message,
                Exception = exception
            };
        }

        public static ScanTestScenarioResult Skip(ScanTestScenario scenario, string reason)
        {
            return new ScanTestScenarioResult
            {
                Scenario = scenario,
                State = ScanTestState.Skip,
                Reason = reason
            };
        }
    }
}
