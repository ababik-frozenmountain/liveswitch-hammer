﻿using System;
using System.Collections.Generic;
using System.Text;

namespace FM.LiveSwitch.Hammer
{
    class MediaServerInfo
    {
        public string Id { get; set; }

        public bool Available { get; set; }

        public bool Active { get; set; }

        public bool OverCapacity { get; set; }

        public double UsedCapacity { get; set; }

        public bool Draining { get; set; }
    }
}
