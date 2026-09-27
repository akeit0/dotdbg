namespace DbgTargetShapeApp;

public class Circle : Shape, IScalable
{
    private double _radius;

    public Circle(double radius)
        : base("Circle")
    {
        if (radius <= 0)
            throw new InvalidShapeException("Circle radius must be positive", radius);

        _radius = radius;
    }

    public double Radius
    {
        get => _radius;
        set
        {
            if (value <= 0)
                throw new InvalidShapeException("Circle radius must be positive", value);
            _radius = value;
        }
    }

    public override double Area => Math.PI * _radius * _radius;

    public override double Perimeter => 2 * Math.PI * _radius;

    public void Scale(double factor)
    {
        if (factor <= 0)
            throw new ArgumentOutOfRangeException(nameof(factor), "Scale factor must be positive");

        _radius *= factor;
    }

    public override string ToString() => $"Circle(r={_radius:F2})";
}
