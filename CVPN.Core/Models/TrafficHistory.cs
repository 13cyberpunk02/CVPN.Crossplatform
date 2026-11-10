namespace CVPN.Models;

/// <summary>
/// Скорость за последнюю минуту - для графика на главной странице. Кольцевой
/// буфер: новые замеры вытесняют старые, память не растёт сколько ни сиди.
/// Замеры приходят из Clash API раз в секунду, поэтому 60 точек - ровно минута.
/// </summary>
public sealed class TrafficHistory
{
    private readonly long[] _up;
    private readonly long[] _down;
    private int _start;

    public TrafficHistory(int capacity = 60)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);

        _up = new long[capacity];
        _down = new long[capacity];
    }

    /// <summary>Сколько точек помещается на график.</summary>
    public int Capacity => _up.Length;

    /// <summary>Сколько точек уже есть.</summary>
    public int Count { get; private set; }

    /// <summary>График подписан на это событие и перерисовывается сам.</summary>
    public event Action? Changed;

    /// <summary>Отдача, i = 0 - самый старый замер.</summary>
    public long Up(int i) => _up[Index(i)];

    /// <summary>Приём, i = 0 - самый старый замер.</summary>
    public long Down(int i) => _down[Index(i)];

    /// <summary>Наибольшая скорость в окне - по ней выбирается масштаб.</summary>
    public long Max
    {
        get
        {
            long max = 0;

            for (var i = 0; i < Count; i++)
                max = Math.Max(max, Math.Max(Up(i), Down(i)));

            return max;
        }
    }

    public void Push(long up, long down)
    {
        var slot = (_start + Count) % Capacity;

        _up[slot] = Math.Max(0, up);
        _down[slot] = Math.Max(0, down);

        if (Count < Capacity) Count++;
        else _start = (_start + 1) % Capacity;

        Changed?.Invoke();
    }

    public void Clear()
    {
        _start = 0;
        Count = 0;

        Changed?.Invoke();
    }

    private int Index(int i)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(i);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(i, Count);

        return (_start + i) % Capacity;
    }

    /// <summary>
    /// Верх шкалы: ближайшее «круглое» значение не меньше максимума (1, 2, 3, 4, 5, 6, 8
    /// и их десятки), умноженное на килобайт или мегабайт. Так подпись читается сразу
    /// («2 МБ/с»), а график не упирается в потолок. Минимум - 64 КБ/с: на пустом
    /// канале иначе вся шкала была бы шумом в пару байт.
    /// </summary>
    public static long NiceCeiling(long max)
    {
        const long floor = 64 * 1024;
        if (max <= floor) return floor;

        long unit = 1024;
        while (unit * 1000 < max) unit *= 1024;

        foreach (var step in (ReadOnlySpan<long>)[1, 2, 3, 4, 5, 6, 8, 10, 15, 20, 30, 40, 50, 60, 80, 100, 150, 200, 300, 400, 500, 600, 800, 1000])
        {
            if (step * unit >= max) return step * unit;
        }

        return 1024 * unit;
    }
}
