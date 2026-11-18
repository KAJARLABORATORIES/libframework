namespace kajarlabs.Framework.Input;

/// <summary>
/// Represents a canonical, order-independent sequence of <see cref="InputKey"/>s forming a key binding chord.
/// </summary>
public readonly struct KeyCombination : IEquatable<KeyCombination>
{
    private readonly InputKey[]? keys;

    /// <summary>
    /// Initializes a new <see cref="KeyCombination"/> from <paramref name="keys"/>,
    /// removing empty entries, deduplicating, and sorting into canonical order.
    /// </summary>
    public KeyCombination(params ReadOnlySpan<InputKey> keys)
    {
        this.keys = canonicalize(keys);
    }

    /// <summary>
    /// Initializes a new <see cref="KeyCombination"/> from <paramref name="keys"/>.
    /// </summary>
    public KeyCombination(IEnumerable<InputKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var buffer = new List<InputKey>();

        foreach (var key in keys)
            buffer.Add(key);

        this.keys = canonicalize(buffer.ToArray());
    }

    /// <summary>
    /// Gets an empty <see cref="KeyCombination"/> with no keys.
    /// </summary>
    public static KeyCombination Empty => default;

    /// <summary>
    /// Gets the canonicalized keys in this combination.
    /// </summary>
    public IReadOnlyList<InputKey> Keys => keys ?? [];

    /// <summary>
    /// Gets the number of keys in this combination.
    /// </summary>
    public int Count => keys?.Length ?? 0;

    /// <summary>
    /// Gets a value indicating whether this combination includes any mouse button or scroll direction input.
    /// </summary>
    public bool ContainsPositionalKeys
    {
        get
        {
            if (keys is null)
                return false;

            for (var i = 0; i < keys.Length; i++)
            {
                if (keys[i].IsMouse || keys[i].IsScroll)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Gets a value indicating whether this combination includes a scroll direction input.
    /// </summary>
    public bool ContainsScrollKeys
    {
        get
        {
            if (keys is null)
                return false;

            for (var i = 0; i < keys.Length; i++)
            {
                if (keys[i].IsScroll)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Implicitly converts a single <see cref="InputKey"/> to a <see cref="KeyCombination"/>.
    /// </summary>
    public static implicit operator KeyCombination(InputKey key) => new(key);

    /// <summary>
    /// Implicitly converts a single <see cref="Key"/> to a <see cref="KeyCombination"/>.
    /// </summary>
    public static implicit operator KeyCombination(Key key) => new((InputKey)key);

    /// <summary>
    /// Implicitly converts a single <see cref="MouseButton"/> to a <see cref="KeyCombination"/>.
    /// </summary>
    public static implicit operator KeyCombination(MouseButton button) => new((InputKey)button);

    /// <summary>
    /// Implicitly converts a single <see cref="ScrollDirection"/> to a <see cref="KeyCombination"/>.
    /// </summary>
    public static implicit operator KeyCombination(ScrollDirection direction) => new((InputKey)direction);

    /// <summary>
    /// Returns <see langword="true"/> if all keys in this combination are satisfied by <paramref name="state"/>
    /// (and optional <paramref name="activeScrollKey"/> for scroll bindings).
    /// </summary>
    public bool IsPressed(in InputState state, InputKey? activeScrollKey = null)
    {
        if (keys is null || keys.Length == 0)
            return false;

        for (var i = 0; i < keys.Length; i++)
        {
            var key = keys[i];

            if (key.IsScroll)
            {
                if (activeScrollKey is not { } scrollKey || !key.Equals(scrollKey))
                    return false;
            }
            else if (!key.IsPressed(in state))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Evaluates whether <paramref name="triggeredKey"/> together with <paramref name="state"/> matches this combination.
    /// </summary>
    /// <param name="triggeredKey">The input key/button/scroll that just fired.</param>
    /// <param name="state">The current input state snapshot.</param>
    /// <param name="requireExact">When <see langword="true"/>, rejects any extra held keys, modifiers, or mouse buttons outside this combination.</param>
    public bool Matches(InputKey triggeredKey, in InputState state, bool requireExact = false)
    {
        if (keys is null || keys.Length == 0)
            return false;

        var matchedTrigger = false;

        for (var i = 0; i < keys.Length; i++)
        {
            if (keys[i].Matches(triggeredKey))
            {
                matchedTrigger = true;
                break;
            }
        }

        if (!matchedTrigger)
            return false;

        var activeScroll = triggeredKey.IsScroll ? triggeredKey : (InputKey?)null;

        if (!IsPressed(in state, activeScroll))
            return false;

        if (!requireExact)
            return true;

        return hasNoExtraPressedInputs(in state);
    }

    /// <summary>
    /// Determines whether two <see cref="KeyCombination"/> instances have the same canonical keys.
    /// </summary>
    public static bool operator ==(KeyCombination left, KeyCombination right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="KeyCombination"/> instances have different canonical keys.
    /// </summary>
    public static bool operator !=(KeyCombination left, KeyCombination right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(KeyCombination other)
    {
        var leftKeys = keys ?? [];
        var rightKeys = other.keys ?? [];

        if (leftKeys.Length != rightKeys.Length)
            return false;

        for (var i = 0; i < leftKeys.Length; i++)
        {
            if (!leftKeys[i].Equals(rightKeys[i]))
                return false;
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is KeyCombination other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        if (keys is null || keys.Length == 0)
            return 0;

        var hash = new HashCode();

        for (var i = 0; i < keys.Length; i++)
            hash.Add(keys[i]);

        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        if (keys is null || keys.Length == 0)
            return string.Empty;

        return string.Join(" + ", keys);
    }

    private bool hasNoExtraPressedInputs(in InputState state)
    {
        var pressedKeyCount = state.Keyboard.PressedKeyCount;

        if (pressedKeyCount > 0)
        {
            Span<Key> pressedKeys = stackalloc Key[pressedKeyCount];
            var written = state.Keyboard.GetPressedKeys(pressedKeys);

            for (var i = 0; i < written; i++)
            {
                var pressedInputKey = InputKey.FromKey(pressedKeys[i]);

                if (!containsMatchingKey(pressedInputKey))
                    return false;
            }
        }

        var activeModifiers = state.Keyboard.Modifiers;

        if (activeModifiers != KeyModifiers.None)
        {
            var coveredModifiers = KeyModifiers.None;

            for (var i = 0; i < keys!.Length; i++)
                coveredModifiers |= keys[i].Modifier;

            if ((activeModifiers & ~coveredModifiers) != 0)
                return false;
        }

        var pressedButtonCount = state.Mouse.PressedButtonCount;

        if (pressedButtonCount > 0)
        {
            Span<MouseButton> pressedButtons = stackalloc MouseButton[pressedButtonCount];
            var written = state.Mouse.GetPressedButtons(pressedButtons);

            for (var i = 0; i < written; i++)
            {
                var pressedButtonKey = InputKey.FromMouseButton(pressedButtons[i]);

                if (!containsMatchingKey(pressedButtonKey))
                    return false;
            }
        }

        return true;
    }

    private bool containsMatchingKey(InputKey physicalKey)
    {
        for (var i = 0; i < keys!.Length; i++)
        {
            if (keys[i].Matches(physicalKey))
                return true;
        }

        return false;
    }

    private static InputKey[]? canonicalize(ReadOnlySpan<InputKey> source)
    {
        if (source.IsEmpty)
            return null;

        var buffer = new InputKey[source.Length];
        var count = 0;

        for (var i = 0; i < source.Length; i++)
        {
            var candidate = source[i];

            if (candidate.Kind == InputKeyKind.None)
                continue;

            var duplicate = false;

            for (var j = 0; j < count; j++)
            {
                if (buffer[j].Equals(candidate))
                {
                    duplicate = true;
                    break;
                }
            }

            if (!duplicate)
                buffer[count++] = candidate;
        }

        if (count == 0)
            return null;

        Array.Sort(buffer, 0, count);
        return count == buffer.Length ? buffer : buffer[..count];
    }
}
