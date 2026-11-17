namespace kajarlabs.Framework.Bindables;

/// <summary>
/// A reactive, bidirectionally bindable value container supporting change notifications,
/// default values, disabled state propagation, and cycle-safe peer synchronization.
/// </summary>
public class Bindable<T> : IBindable<T>
{
    private readonly Lock bindingsLock = new();
    private readonly List<WeakReference<Bindable<T>>> bindings = [];

    private T currentValue;
    private T defaultValue;
    private bool disabled;

    /// <summary>
    /// Initializes a new instance of the <see cref="Bindable{T}"/> class.
    /// </summary>
    /// <param name="defaultValue">The initial <see cref="Value"/> and <see cref="Default"/>.</param>
    public Bindable(T defaultValue = default!)
    {
        currentValue = defaultValue;
        this.defaultValue = defaultValue;
    }

    /// <inheritdoc />
    public event Action<ValueChangedEvent<T>>? ValueChanged;

    /// <inheritdoc />
    public event Action<bool>? DisabledChanged;

    /// <summary>
    /// Occurs when <see cref="Default"/> changes.
    /// </summary>
    public event Action<T>? DefaultChanged;

    /// <summary>
    /// Gets or sets the current value. Setting a value while <see cref="Disabled"/> is <see langword="true"/>
    /// throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public T Value
    {
        get => currentValue;
        set => setValue(value, bypassChecks: false, source: null);
    }

    /// <summary>
    /// Gets or sets the default value used by <see cref="SetDefault"/> and <see cref="IsDefault"/>.
    /// </summary>
    public T Default
    {
        get => defaultValue;
        set => setDefault(value, source: null);
    }

    /// <summary>
    /// Gets or sets a value indicating whether direct mutations to <see cref="Value"/> and <see cref="SetDefault"/> are blocked.
    /// </summary>
    public bool Disabled
    {
        get => disabled;
        set => setDisabled(value, source: null);
    }

    /// <inheritdoc />
    public virtual bool IsDefault => EqualityComparer<T>.Default.Equals(currentValue, defaultValue);

    /// <summary>
    /// Resets <see cref="Value"/> to <see cref="Default"/>.
    /// </summary>
    public virtual void SetDefault() => Value = Default;

    /// <inheritdoc />
    public void BindValueChanged(Action<ValueChangedEvent<T>> onChange, bool runOnceImmediately = false)
    {
        ArgumentNullException.ThrowIfNull(onChange);

        ValueChanged += onChange;

        if (runOnceImmediately)
            onChange(new ValueChangedEvent<T>(currentValue, currentValue));
    }

    /// <inheritdoc />
    public void BindDisabledChanged(Action<bool> onChange, bool runOnceImmediately = false)
    {
        ArgumentNullException.ThrowIfNull(onChange);

        DisabledChanged += onChange;

        if (runOnceImmediately)
            onChange(disabled);
    }

    /// <summary>
    /// Binds this bindable bidirectionally to <paramref name="them"/>, synchronizing initial state from <paramref name="them"/>.
    /// </summary>
    public virtual void BindTo(Bindable<T> them)
    {
        ArgumentNullException.ThrowIfNull(them);

        if (ReferenceEquals(this, them))
            throw new ArgumentException("Cannot bind a bindable to itself.", nameof(them));

        if (isBoundTo(them))
            throw new ArgumentException("This bindable is already bound to the target bindable.", nameof(them));

        SyncFrom(them);

        addBinding(them);
        them.addBinding(this);
    }

    /// <inheritdoc />
    public void BindTo(IBindable<T> them)
    {
        ArgumentNullException.ThrowIfNull(them);

        if (them is not Bindable<T> concrete)
            throw new ArgumentException($"Unsupported {nameof(IBindable<T>)} implementation '{them.GetType()}'.", nameof(them));

        BindTo(concrete);
    }

    /// <summary>
    /// Creates a new instance of the same bindable type bound bidirectionally to this instance.
    /// </summary>
    public Bindable<T> GetBoundCopy()
    {
        var copy = CreateInstance();
        copy.BindTo(this);
        return copy;
    }

    /// <inheritdoc />
    IBindable<T> IBindable<T>.GetBoundCopy() => GetBoundCopy();

    /// <inheritdoc />
    public virtual void UnbindEvents()
    {
        ValueChanged = null;
        DisabledChanged = null;
        DefaultChanged = null;
    }

    /// <inheritdoc />
    public void UnbindBindings()
    {
        List<Bindable<T>> peers;

        lock (bindingsLock)
        {
            if (bindings.Count == 0)
                return;

            peers = copyLivePeersLocked();
            bindings.Clear();
        }

        for (var i = 0; i < peers.Count; i++)
            peers[i].removeBinding(this);
    }

    /// <inheritdoc />
    public void UnbindAll()
    {
        UnbindEvents();
        UnbindBindings();
    }

    /// <summary>
    /// Creates a fresh instance of this concrete bindable type for <see cref="GetBoundCopy"/> without reflection.
    /// </summary>
    protected virtual Bindable<T> CreateInstance() => new();

    /// <summary>
    /// Normalizes or clamps a candidate value before comparison and propagation.
    /// </summary>
    protected virtual T NormalizeValue(T value) => value;

    /// <summary>
    /// Re-normalizes <see cref="Value"/> after constraint changes in subclasses such as <see cref="BindableNumber{T}"/>.
    /// </summary>
    protected void RevalidateValue()
        => setValue(currentValue, bypassChecks: true, source: null);

    /// <summary>
    /// Copies initial configuration and state from <paramref name="source"/> during <see cref="BindTo(Bindable{T})"/>.
    /// </summary>
    protected virtual void SyncFrom(Bindable<T> source)
    {
        setDefault(source.defaultValue, source);
        setValue(source.currentValue, bypassChecks: true, source);
        setDisabled(source.disabled, source);
    }

    /// <summary>
    /// Returns a snapshot list of currently live bound peers for subclass propagation.
    /// </summary>
    protected List<Bindable<T>> GetLivePeers()
    {
        lock (bindingsLock)
            return bindings.Count == 0 ? [] : copyLivePeersLocked();
    }

    private void setValue(T rawValue, bool bypassChecks, Bindable<T>? source)
    {
        if (!bypassChecks && disabled)
            throw new InvalidOperationException("Cannot modify value while bindable is disabled.");

        var normalized = NormalizeValue(rawValue);

        if (EqualityComparer<T>.Default.Equals(currentValue, normalized))
            return;

        var previous = currentValue;
        currentValue = normalized;
        ValueChanged?.Invoke(new ValueChangedEvent<T>(previous, normalized));

        // If normalization/clamping modified the incoming value, notify all peers (including source)
        // so asymmetric bounds converge consistently.
        var wasModifiedByNormalization = source is not null && !EqualityComparer<T>.Default.Equals(rawValue, normalized);
        var peers = GetLivePeers();

        for (var i = 0; i < peers.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(currentValue, normalized))
                break;

            var peer = peers[i];

            if (!wasModifiedByNormalization && ReferenceEquals(peer, source))
                continue;

            peer.setValue(normalized, bypassChecks: true, source: this);
        }
    }

    private void setDefault(T newDefault, Bindable<T>? source)
    {
        if (EqualityComparer<T>.Default.Equals(defaultValue, newDefault))
            return;

        defaultValue = newDefault;
        DefaultChanged?.Invoke(newDefault);

        var peers = GetLivePeers();

        for (var i = 0; i < peers.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(defaultValue, newDefault))
                break;

            var peer = peers[i];

            if (ReferenceEquals(peer, source))
                continue;

            peer.setDefault(newDefault, source: this);
        }
    }

    private void setDisabled(bool newDisabled, Bindable<T>? source)
    {
        if (disabled == newDisabled)
            return;

        disabled = newDisabled;
        DisabledChanged?.Invoke(newDisabled);

        var peers = GetLivePeers();

        for (var i = 0; i < peers.Count; i++)
        {
            if (disabled != newDisabled)
                break;

            var peer = peers[i];

            if (ReferenceEquals(peer, source))
                continue;

            peer.setDisabled(newDisabled, source: this);
        }
    }

    private bool isBoundTo(Bindable<T> candidate)
    {
        lock (bindingsLock)
        {
            for (var i = bindings.Count - 1; i >= 0; i--)
            {
                if (!bindings[i].TryGetTarget(out var target))
                {
                    bindings.RemoveAt(i);
                    continue;
                }

                if (ReferenceEquals(target, candidate))
                    return true;
            }

            return false;
        }
    }

    private void addBinding(Bindable<T> peer)
    {
        lock (bindingsLock)
            bindings.Add(new WeakReference<Bindable<T>>(peer));
    }

    private void removeBinding(Bindable<T> peer)
    {
        lock (bindingsLock)
        {
            for (var i = bindings.Count - 1; i >= 0; i--)
            {
                if (!bindings[i].TryGetTarget(out var target) || ReferenceEquals(target, peer))
                    bindings.RemoveAt(i);
            }
        }
    }

    private List<Bindable<T>> copyLivePeersLocked()
    {
        var result = new List<Bindable<T>>(bindings.Count);

        for (var i = bindings.Count - 1; i >= 0; i--)
        {
            if (!bindings[i].TryGetTarget(out var target))
            {
                bindings.RemoveAt(i);
                continue;
            }

            result.Add(target);
        }

        result.Reverse();
        return result;
    }
}
