using kajarlabs.Framework.Input;

namespace kajarlabs.Framework.Tests.Input;

public sealed class KeyAndModifiersTests
{
    [Fact]
    public void AllKeyEnumValuesFitWithinKeyboardBitsetAndHaveUniqueValues()
    {
        var values = Enum.GetValues<Key>();
        var distinct = new HashSet<int>();

        foreach (var key in values)
        {
            var raw = (int)key;
            Assert.InRange(raw, 0, 255);
            Assert.True(distinct.Add(raw), $"Duplicate Key value {raw} for {key}.");
        }
    }

    [Fact]
    public void AllMouseButtonEnumValuesFitWithinMouseBitmaskAndHaveUniqueValues()
    {
        var values = Enum.GetValues<MouseButton>();
        var distinct = new HashSet<int>();

        foreach (var button in values)
        {
            var raw = (int)button;
            Assert.InRange(raw, 0, 7);
            Assert.True(distinct.Add(raw), $"Duplicate MouseButton value {raw} for {button}.");
        }
    }

    [Theory]
    [InlineData(Key.ShiftLeft, KeyModifiers.Shift)]
    [InlineData(Key.ShiftRight, KeyModifiers.Shift)]
    [InlineData(Key.ControlLeft, KeyModifiers.Control)]
    [InlineData(Key.ControlRight, KeyModifiers.Control)]
    [InlineData(Key.AltLeft, KeyModifiers.Alt)]
    [InlineData(Key.AltRight, KeyModifiers.Alt)]
    [InlineData(Key.SuperLeft, KeyModifiers.Super)]
    [InlineData(Key.SuperRight, KeyModifiers.Super)]
    [InlineData(Key.A, KeyModifiers.None)]
    [InlineData(Key.Space, KeyModifiers.None)]
    [InlineData(Key.Unknown, KeyModifiers.None)]
    public void KeyAndKeyModifiersExtensionMembersMapCorrectly(Key key, KeyModifiers expected)
    {
        Assert.Equal(expected != KeyModifiers.None, key.IsModifier);
        Assert.Equal(expected, key.ToKeyModifiers());
        Assert.Equal(expected, KeyModifiers.FromKey(key));
    }

    [Fact]
    public void KeyModifiersExtensionFlagsReflectCombinedState()
    {
        var combo = KeyModifiers.Shift | KeyModifiers.Alt;

        Assert.True(combo.HasShift);
        Assert.False(combo.HasControl);
        Assert.True(combo.HasAlt);
        Assert.False(combo.HasSuper);

        Assert.True(KeyModifiers.All.HasShift);
        Assert.True(KeyModifiers.All.HasControl);
        Assert.True(KeyModifiers.All.HasAlt);
        Assert.True(KeyModifiers.All.HasSuper);

        Assert.False(KeyModifiers.None.HasShift);
        Assert.False(KeyModifiers.None.HasControl);
        Assert.False(KeyModifiers.None.HasAlt);
        Assert.False(KeyModifiers.None.HasSuper);
    }

    [Fact]
    public void PublicInputApiDoesNotExposeSdlTypes()
    {
        var assembly = typeof(InputState).Assembly;
        var inputTypes = assembly.GetExportedTypes()
            .Where(t => t.Namespace == typeof(InputState).Namespace);

        foreach (var type in inputTypes)
        {
            Assert.DoesNotContain("SDL", type.FullName, StringComparison.OrdinalIgnoreCase);

            foreach (var prop in type.GetProperties())
                Assert.DoesNotContain("SDL3", prop.PropertyType.AssemblyQualifiedName ?? string.Empty, StringComparison.OrdinalIgnoreCase);

            foreach (var method in type.GetMethods())
            {
                Assert.DoesNotContain("SDL3", method.ReturnType.AssemblyQualifiedName ?? string.Empty, StringComparison.OrdinalIgnoreCase);

                foreach (var param in method.GetParameters())
                    Assert.DoesNotContain("SDL3", param.ParameterType.AssemblyQualifiedName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
