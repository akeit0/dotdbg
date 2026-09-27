namespace DbgTargetShapeApp;

public abstract class Shape
{
    public string Name { get; }

    protected Shape(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public abstract double Area { get; }

    public abstract double Perimeter { get; }

    public virtual string Describe()
    {
        return $"{Name}: area={Area:F2}, perimeter={Perimeter:F2}";
    }
}

public interface IScalable
{
    void Scale(double factor);
}
