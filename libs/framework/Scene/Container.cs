namespace kajarlabs.Framework.Scene;

/// <summary>
/// A composite container exposing typed child management (<see cref="Children"/>, <see cref="Add"/>,
/// <see cref="Remove"/>, <see cref="Clear"/>) with optional <see cref="Content"/> redirection so subclasses
/// can isolate internal decoration drawables from user-provided children.
/// </summary>
public class Container<T> : CompositeDrawable
    where T : Drawable
{
    private readonly List<T>? typedChildren = typeof(T) == typeof(Drawable) ? null : [];
    private long syncedChildrenVersion = -1;

    /// <summary>
    /// Gets the child drawables exposed by this container (or by <see cref="Content"/> when redirected).
    /// Replacing this property clears existing children and adds the supplied sequence.
    /// </summary>
    public IReadOnlyList<T> Children
    {
        get
        {
            if (!ReferenceEquals(Content, this))
                return Content.Children;

            if (typedChildren is null)
                return (IReadOnlyList<T>)InternalChildren;

            syncTypedChildren();
            return typedChildren;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            Clear();
            AddRange(value);
        }
    }

    /// <summary>
    /// Sets a single child drawable, clearing any previously added children.
    /// </summary>
    public T Child
    {
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            Clear();
            Add(value);
        }
    }

    /// <summary>
    /// Gets the target container to which <see cref="Children"/>, <see cref="Add"/>, <see cref="AddRange"/>,
    /// <see cref="Remove"/>, and <see cref="Clear"/> forward. Subclasses override this to separate
    /// <see cref="CompositeDrawable.InternalChildren"/> from user <see cref="Children"/>.
    /// </summary>
    protected virtual Container<T> Content => this;

    /// <summary>
    /// Adds a child to this container (or its <see cref="Content"/> target).
    /// </summary>
    public virtual void Add(T child)
    {
        ArgumentNullException.ThrowIfNull(child);

        if (!ReferenceEquals(Content, this))
        {
            Content.Add(child);
            return;
        }

        AddInternal(child);
    }

    /// <summary>
    /// Adds multiple children to this container (or its <see cref="Content"/> target).
    /// </summary>
    public void AddRange(IEnumerable<T> children)
    {
        ArgumentNullException.ThrowIfNull(children);

        if (!ReferenceEquals(Content, this))
        {
            Content.AddRange(children);
            return;
        }

        foreach (var child in children)
            Add(child);
    }

    /// <summary>
    /// Removes <paramref name="child"/> from this container (or its <see cref="Content"/> target)
    /// and optionally disposes it.
    /// </summary>
    public virtual bool Remove(T child, bool disposeImmediately = false)
    {
        ArgumentNullException.ThrowIfNull(child);

        if (!ReferenceEquals(Content, this))
            return Content.Remove(child, disposeImmediately);

        return RemoveInternal(child, disposeImmediately);
    }

    /// <summary>
    /// Removes all children from this container (or its <see cref="Content"/> target)
    /// and optionally disposes them.
    /// </summary>
    public virtual void Clear(bool disposeImmediately = true)
    {
        if (!ReferenceEquals(Content, this))
        {
            Content.Clear(disposeImmediately);
            return;
        }

        ClearInternal(disposeImmediately);
    }

    /// <inheritdoc />
    protected override void OnChildrenChanged()
    {
        syncedChildrenVersion = -1;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!disposing || IsDisposed)
            return;

        typedChildren?.Clear();
        base.Dispose(disposing);
    }

    private void syncTypedChildren()
    {
        if (typedChildren is null)
            return;

        var currentVersion = ChildrenVersion;

        if (syncedChildrenVersion == currentVersion)
            return;

        typedChildren.Clear();
        var internalList = InternalChildren;

        for (var i = 0; i < internalList.Count; i++)
        {
            if (internalList[i] is T typed)
                typedChildren.Add(typed);
        }

        syncedChildrenVersion = currentVersion;
    }
}

/// <summary>
/// A non-generic composite container holding arbitrary <see cref="Drawable"/> children.
/// </summary>
public class Container : Container<Drawable>
{
}
