// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
namespace MutSea.LibrarySmoke;

public static class CapabilityResponse
{
    // The inherited HTTP dispatcher can emit an empty HTTP 200 after a simple
    // handler has been removed. World API success always has a JSON envelope.
    // Do not accept arbitrary HTML, malformed data or a successful JSON result.
    public static bool Unavailable(int status,ReadOnlySpan<byte> body) =>
        status is 400 or 401 or 403 or 404 or 410 || status==200&&body.IsEmpty;
}
