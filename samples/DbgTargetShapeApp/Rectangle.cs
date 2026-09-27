namespace DbgTargetShapeApp;

public class Rectangle : Shape, IScalable
{
    private double _width;
    private double _height;

    public Rectangle(double width, double height)
        : base("Rectangle")
    {
        if (width <= 0 || height <= 0)
            throw new InvalidShapeException(
                "Rectangle width and height must be positive",
                (width, height)
            );

        _width = width;
        _height = height;
    }

    public double Width => _width;

    public double Height => _height;

    public override double Area => _width * _height;

    public override double Perimeter => 2 * (_width + _height);

    public bool IsSquare => Math.Abs(_width - _height) < double.Epsilon;

    public void Scale(double factor)
    {
        if (factor <= 0)
            throw new ArgumentOutOfRangeException(nameof(factor), "Scale factor must be positive");

        _width *= factor;
        _height *= factor;
    }

    public override string ToString() => $"Rectangle({_width:F2} x {_height:F2})";
}
