/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using MutSea.Framework;
using MutSea.Region.Framework.Interfaces;
using MutSea.Region.Framework.Scenes;

namespace MutSea.Region.CoreModules.World.Terrain.PaintBrushes
{
    public class NoiseSphere : ITerrainPaintableEffect
    {
        #region ITerrainPaintableEffect Members

        public void PaintEffect(ITerrainChannel map, bool[,] mask, float rx, float ry, float rz,
            float size, float strength, int startX, int endX, int startY, int endY)
        {
            int x, y;
            float distancefactor;
            float dx2;

            size *= size;

            for (x = startX; x <= endX; x++)
            {
                dx2 = (x - rx) * (x - rx);
                for (y = startY; y <= endY; y++)
                {
                    if (!mask[x, y])
                        continue;

                    // Calculate a sphere and add it to the heighmap
                    distancefactor = (dx2 + (y - ry) * (y - ry)) / size;
                    if (distancefactor > 1.0f)
                        continue;

                    distancefactor = strength * (1.0f - distancefactor);
                    float noise = (float)TerrainUtil.PerlinNoise2D(x / (double) map.Width, y / (double) map.Height, 8, 1.0);
                    map[x, y] += noise * distancefactor;
                }
            }
        }

        #endregion
    }
}
