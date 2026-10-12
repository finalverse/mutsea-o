// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using MutSea.Framework;

namespace Finalverse.World;
// Existing native Light extra parameter; linear RGB matches the current
// viewer's LLLightParams::unpack/getLightLinearColor contract. Intensity is
// packed separately in alpha. Emissive material is a separate surface factor.
public static class ContentLightAdapter
{
    public static void Bind(PrimitiveBaseShape shape,ContentLight? light)
    {
        if(light==null)return;
        ContentCatalog.ValidateLight(light);
        shape.LightEntry=true;
        shape.LightColorR=(float)light.color_linear[0];
        shape.LightColorG=(float)light.color_linear[1];
        shape.LightColorB=(float)light.color_linear[2];
        shape.LightColorA=1;
        shape.LightIntensity=(float)light.intensity;
        shape.LightRadius=(float)light.radius;
        shape.LightFalloff=(float)light.falloff;
        shape.LightCutoff=0;
    }
}
