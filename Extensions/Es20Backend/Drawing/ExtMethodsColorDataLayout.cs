﻿using Duality.Drawing;
using OpenTK.Graphics.ES20;

namespace Duality.Backend.Es20
{
    public static class ExtMethodColorDataLayout
	{
		public static PixelFormat ToOpenTK(this ColorDataLayout layout)
		{
			switch (layout)
			{
				default:
				case ColorDataLayout.Rgba: return PixelFormat.Rgba;
			}
		}
	}
}