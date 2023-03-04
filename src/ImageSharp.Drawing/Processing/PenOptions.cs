// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Options for the Pen
    /// </summary>
    public class PenOptions
    {
        public PenOptions(float strokeWidth)
            : this(Color.Black, strokeWidth)
        {
        }

        public PenOptions(Color color, float strokeWidth)
            : this(color, strokeWidth, null)
        {
        }

        public PenOptions(Color color, float strokeWidth, IEnumerable<float>? strokePattern)
            : this(new SolidBrush(color), strokeWidth, strokePattern)
        {
        }

        public PenOptions(Brush strokeFill, float strokeWidth, IEnumerable<float>? strokePattern)
        {
            this.StrokeFill = strokeFill;
            this.StrokeWidth = strokeWidth;
            this.StrokePattern = strokePattern;
        }

        public Brush StrokeFill { get; set; } // defaults to black solid brush when undefined

        public float StrokeWidth { get; set; }

        IEnumerable<float>? StrokePattern { get; set; }

        public JointStyle? JointStyle { get; set; }

        public EndCapStyle? EndCap { get; set; }
    }
}
