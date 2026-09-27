namespace DbgTargetShapeApp;

public class ShapeService
{
    private readonly List<Shape> _shapes = new();

    public event EventHandler<ShapeEventArgs>? ShapeAdded;

    public IReadOnlyList<Shape> Shapes => _shapes.AsReadOnly();

    public void Add(Shape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        _shapes.Add(shape);
        OnShapeAdded(shape);
    }

    public double TotalArea()
    {
        var total = 0.0;
        foreach (var shape in _shapes)
        {
            total += shape.Area;
        }
        return total;
    }

    public double AveragePerimeter()
    {
        if (_shapes.Count == 0)
            return 0.0;

        var sum = 0.0;
        for (var i = 0; i < _shapes.Count; i++)
        {
            sum += _shapes[i].Perimeter;
        }
        return sum / _shapes.Count;
    }

    public Shape? FindLargest() =>
        _shapes.Count == 0 ? null : _shapes.OrderByDescending(s => s.Area).First();

    public IEnumerable<Shape> ScaledBy(double factor)
    {
        foreach (var shape in _shapes)
        {
            if (shape is IScalable scalable)
            {
                scalable.Scale(factor);
            }
            yield return shape;
        }
    }

    public async Task<IReadOnlyList<ShapeMetrics>> ComputeMetricsAsync(
        CancellationToken cancellationToken = default
    )
    {
        await Task.Delay(10, cancellationToken).ConfigureAwait(false);

        var metrics = new List<ShapeMetrics>();
        for (var index = 0; index < _shapes.Count; index++)
        {
            var shape = _shapes[index];
            metrics.Add(new ShapeMetrics(shape.Name, shape.Area, shape.Perimeter));
        }

        return metrics.AsReadOnly();
    }

    public void ScaleAll(double factor)
    {
        var copy = new List<Shape>(_shapes);
        foreach (var shape in copy)
        {
            if (shape is IScalable scalable)
            {
                scalable.Scale(factor);
            }
        }
    }

    private void OnShapeAdded(Shape shape)
    {
        ShapeAdded?.Invoke(this, new ShapeEventArgs(shape));
    }
}

public sealed class ShapeEventArgs : EventArgs
{
    public Shape Shape { get; }

    public ShapeEventArgs(Shape shape)
    {
        Shape = shape;
    }
}

public readonly record struct ShapeMetrics(string Name, double Area, double Perimeter);
