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

using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using MutSea.Region.Framework.Interfaces;

namespace MutSea.Region.CoreModules.World.Terrain.FileLoaders
{
    /// <summary>
    /// A generic windows bitmap loader.
    /// Should be capable of handling 24-bit RGB images.
    ///
    /// Uses the System.Drawing filesystem loader.
    /// </summary>
    internal class BMP : GenericSystemDrawing
    {
        /// <summary>
        /// Exports a file to a image on the disk using a System.Drawing exporter.
        /// </summary>
        /// <param name="filename">The target filename</param>
        /// <param name="map">The terrain channel being saved</param>
        public override void SaveFile(string filename, ITerrainChannel map)
        {
            using(Bitmap colours = CreateGrayscaleBitmapFromMap(map))
                colours.Save(filename,ImageFormat.Bmp);
        }

        /// <summary>
        /// Exports a stream using a System.Drawing exporter.
        /// </summary>
        /// <param name="stream">The target stream</param>
        /// <param name="map">The terrain channel being saved</param>
        public override void SaveStream(Stream stream, ITerrainChannel map)
        {
            using(Bitmap colours = CreateGrayscaleBitmapFromMap(map))
                colours.Save(stream,ImageFormat.Bmp);
        }

        /// <summary>
        /// The human readable version of the file format(s) this loader handles
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return "BMP";
        }

        //Returns true if this extension is supported for terrain save-tile
        public override bool SupportsTileSave()
        {
            return false;
        }
    }
}
