using System;
using FischMacroCS.Native;
using Xunit;

namespace FischMacroCS.Tests;

public class CoordinateSafetyTests
{
    [Theory]
    [InlineData("F1", Win32.VK_F1)]
    [InlineData("F2", Win32.VK_F2)]
    [InlineData("F5", Win32.VK_F5)]
    [InlineData("F6", Win32.VK_F6)]
    [InlineData("F7", Win32.VK_F7)]
    [InlineData("F12", Win32.VK_F12)]
    [InlineData("INSERT", Win32.VK_INSERT)]
    [InlineData("DELETE", Win32.VK_DELETE)]
    [InlineData("HOME", Win32.VK_HOME)]
    [InlineData("END", Win32.VK_END)]
    [InlineData("PAUSE", Win32.VK_PAUSE)]
    [InlineData("unknown_key", Win32.VK_F6)] // Defaults to F6
    public void ParseVirtualKey_ParsesCorrectly(string keyName, uint expectedVk)
    {
        uint vk = Win32.ParseVirtualKey(keyName);
        Assert.Equal(expectedVk, vk);
    }

    [Fact]
    public void SanitizeGameCoordinate_NullHwnd_ReturnsFalse()
    {
        bool success = Win32.SanitizeGameCoordinate(IntPtr.Zero, 100, 100, out _, out _, out _, out _);
        Assert.False(success);
    }

    [Fact]
    public void SafeClientToScreen_InvalidHwnd_ReturnsInputCoordsWithoutMutation()
    {
        int inX = 250;
        int inY = 400;
        bool success = Win32.SafeClientToScreen(IntPtr.Zero, inX, inY, out int outX, out int outY);

        // When hwnd is invalid, fallback returns input coordinates
        Assert.False(success);
        Assert.Equal(inX, outX);
        Assert.Equal(inY, outY);
    }
}
