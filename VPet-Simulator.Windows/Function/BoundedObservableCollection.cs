using System.Collections.ObjectModel;

namespace VPet_Simulator.Windows;

/// <summary>
/// ObservableCollection à taille maximale : les plus anciens éléments sont retirés au-delà de <see cref="MaxCount"/>.
/// Les ajouts sont sérialisés (la collection est alimentée depuis plusieurs threads).
/// </summary>
public class BoundedObservableCollection<T> : ObservableCollection<T>
{
    private readonly object _lock = new();

    public int MaxCount { get; }

    public BoundedObservableCollection(int maxCount) => MaxCount = maxCount;

    protected override void InsertItem(int index, T item)
    {
        lock (_lock)
        {
            base.InsertItem(index, item);
            while (Count > MaxCount)
                base.RemoveItem(0);
        }
    }

    protected override void RemoveItem(int index)
    {
        lock (_lock)
            base.RemoveItem(index);
    }

    protected override void ClearItems()
    {
        lock (_lock)
            base.ClearItems();
    }
}
