﻿// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace Microsoft.DotNet.Interactive.Commands
{
    public abstract class KernelCommand
    {
        private string _targetKernelName;

        protected KernelCommand(string targetKernelName = null, KernelCommand parent = null)
        {
            Properties = new Dictionary<string, object>(StringComparer.InvariantCultureIgnoreCase);
            _targetKernelName = targetKernelName;
            Parent = parent;
        }

        [JsonIgnore]
        public KernelCommandInvocation Handler { get; set; }

        [JsonIgnore]
        public KernelCommand Parent { get; internal set; }

        [JsonIgnore]
        public bool? ShouldPublishCompletionEvent { get; protected internal set; }

        [JsonIgnore]
        public IDictionary<string, object> Properties { get; }

        public string TargetKernelName
        {
            get => _targetKernelName;
            internal set => _targetKernelName = value;
        }

        internal static KernelCommand None => new NoCommand();

        [JsonIgnore]
        internal Uri OriginUri { get; set; }

        [JsonIgnore]
        internal Uri DestinationUri { get; set; }

        [JsonIgnore]
        internal SchedulingScope SchedulingScope { get; set; }

        public virtual Task InvokeAsync(KernelInvocationContext context)
        {
            if (Handler is null)
            {
                throw new NoSuitableKernelException(this);
            }

            return Handler(this, context);
        }
    }
}
