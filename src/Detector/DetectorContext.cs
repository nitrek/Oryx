﻿// --------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// --------------------------------------------------------------------------------------------

using Microsoft.Oryx.Common;

namespace Microsoft.Oryx.Detector
{
    /// <summary>
    /// Context to create a detector.
    /// </summary>
    public class DetectorContext
    {
        public ISourceRepo SourceRepo { get; set; }
    }
}