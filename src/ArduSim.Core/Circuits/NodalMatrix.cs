namespace ArduSim.Core.Circuits;

/// <summary>
/// The conductance equations of one island of unknown nets (ADR-017). The matrix is symmetric and
/// positive definite (every net leaks to ground), so elimination needs no pivoting.
/// </summary>
internal sealed class NodalMatrix
{
    private double[] _a = new double[16];
    private double[] _b = new double[4];
    private double[] _unit = new double[4];
    private int _size;

    /// <summary>The net voltages after <see cref="Solve"/>.</summary>
    public double[] Solution => _b;

    public void Clear(int size)
    {
        if (size > _b.Length)
        {
            int capacity = Math.Max(size, _b.Length * 2);
            _a = new double[capacity * capacity];
            _b = new double[capacity];
            _unit = new double[capacity];
        }
        else
        {
            Array.Clear(_a, 0, size * size);
            Array.Clear(_b, 0, size);
        }

        _size = size;
    }

    /// <summary>A conductance from a net to ground.</summary>
    public void Leak(int net, double conductance) => _a[(net * _size) + net] += conductance;

    /// <summary>A conductance from a net to a source of known voltage.</summary>
    public void ToSource(int net, double conductance, double voltage)
    {
        _a[(net * _size) + net] += conductance;
        _b[net] += conductance * voltage;
    }

    public void Between(int a, int b, double conductance)
    {
        _a[(a * _size) + a] += conductance;
        _a[(b * _size) + b] += conductance;
        _a[(a * _size) + b] -= conductance;
        _a[(b * _size) + a] -= conductance;
    }

    /// <summary>Current flowing into a net.</summary>
    public void Inject(int net, double amps) => _b[net] += amps;

    /// <summary>Eliminates in place, then solves for <see cref="Solution"/>.</summary>
    public void Solve()
    {
        int n = _size;
        double[] a = _a;
        for (int k = 0; k < n; k++)
        {
            double pivot = a[(k * n) + k];
            for (int i = k + 1; i < n; i++)
            {
                double factor = a[(i * n) + k] / pivot;
                a[(i * n) + k] = factor;
                if (factor == 0)
                {
                    continue;
                }

                for (int j = k + 1; j < n; j++)
                {
                    a[(i * n) + j] -= factor * a[(k * n) + j];
                }
            }
        }

        Substitute(_b);
    }

    /// <summary>
    /// The resistance seen from a net with every source at zero: a diagonal entry of the inverse.
    /// Valid after <see cref="Solve"/>.
    /// </summary>
    public double ResistanceAt(int net)
    {
        Array.Clear(_unit, 0, _size);
        _unit[net] = 1;
        Substitute(_unit);
        return _unit[net];
    }

    private void Substitute(double[] x)
    {
        int n = _size;
        double[] a = _a;
        for (int i = 1; i < n; i++)
        {
            double sum = x[i];
            for (int j = 0; j < i; j++)
            {
                sum -= a[(i * n) + j] * x[j];
            }

            x[i] = sum;
        }

        for (int i = n - 1; i >= 0; i--)
        {
            double sum = x[i];
            for (int j = i + 1; j < n; j++)
            {
                sum -= a[(i * n) + j] * x[j];
            }

            x[i] = sum / a[(i * n) + i];
        }
    }
}
