// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace Microsoft.DotNet.Interactive.Jupyter
{
    public class Session
    {
        public string Id { get; }

        public string Key { get; set; }

        public string Username { get; }

        public static string ProtocolVersion => "5.0";

        public Session(string username = "username")
        {
            Id = Guid.NewGuid().ToString();
            Username = username;
        }
    }
}
