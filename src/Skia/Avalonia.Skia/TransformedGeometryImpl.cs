using Avalonia.Platform;
using SkiaSharp;

namespace Avalonia.Skia
{
    /// <summary>
    /// A Skia implementation of a <see cref="ITransformedGeometryImpl"/>.
    /// </summary>
    internal class TransformedGeometryImpl : GeometryImpl, ITransformedGeometryImpl
    {
        private readonly GeometryImpl _source;
        private SKPath? _strokePath;
        private SKPath? _fillPath;
        private Rect _bounds;
        private volatile int _sourceVersion;

        /// <summary>
        ///  Initializes a new instance of the <see cref="TransformedGeometryImpl"/> class.
        /// </summary>
        /// <param name="source">Source geometry.</param>
        /// <param name="transform">Transform of new geometry.</param>
        public TransformedGeometryImpl(GeometryImpl source, Matrix transform)
        {
            _source = source;
            Transform = transform;
            UpdatePaths();
        }

        /// <inheritdoc />
        public override SKPath? StrokePath
        {
            get
            {
                EnsureUpToDate();
                return _strokePath;
            }
        }

        /// <inheritdoc />
        public override SKPath? FillPath
        {
            get
            {
                EnsureUpToDate();
                return _fillPath;
            }
        }

        /// <inheritdoc />
        public IGeometryImpl SourceGeometry => _source;

        /// <inheritdoc />
        public Matrix Transform { get; }

        /// <inheritdoc />
        public override Rect Bounds
        {
            get
            {
                EnsureUpToDate();
                return _bounds;
            }
        }

        private void EnsureUpToDate()
        {
            if (_sourceVersion == _source.Version)
                return;

            UpdatePaths();
            InvalidateCaches();
        }

        private void UpdatePaths()
        {
            // Read before copying the paths: if the source changes meanwhile, the next access updates again.
            var sourceVersion = _source.Version;
            var matrix = Transform.ToSKMatrix();

            var strokePath = _source.StrokePath.Clone();
            strokePath?.Transform(matrix);

            SKPath? fillPath;
            if (ReferenceEquals(_source.StrokePath, _source.FillPath))
                fillPath = strokePath;
            else
            {
                fillPath = _source.FillPath.Clone();
                fillPath?.Transform(matrix);
            }

            _strokePath = strokePath;
            _fillPath = fillPath;
            _bounds = strokePath?.TightBounds.ToAvaloniaRect() ?? default;
            _sourceVersion = sourceVersion;
        }
    }
}
