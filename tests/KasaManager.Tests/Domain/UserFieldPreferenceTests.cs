using KasaManager.Domain.FormulaEngine;
using Xunit;

namespace KasaManager.Tests.Domain;

/// <summary>
/// R17: UserFieldPreference entity unit testleri.
/// </summary>
public class UserFieldPreferenceTests
{
    [Fact]
    public void SetSelectedFields_And_GetSelectedFields_Works()
    {
        // Arrange
        var pref = new UserFieldPreference
        {
            KasaType = "Aksam",
            UserName = "test_user"
        };
        var fields = new List<string> { "normal_tahsilat", "normal_harc", "banka_bakiye" };

        // Act
        pref.SetSelectedFields(fields);
        var result = pref.GetSelectedFields();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Contains("normal_tahsilat", result);
        Assert.Contains("normal_harc", result);
        Assert.Contains("banka_bakiye", result);
    }

    [Fact]
    public void SetSelectedFields_With_Empty_List_Works()
    {
        // Arrange
        var pref = new UserFieldPreference
        {
            KasaType = "Sabah"
        };

        // Act
        pref.SetSelectedFields(new List<string>());
        var result = pref.GetSelectedFields();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetSelectedFields_With_Null_Json_Returns_Empty()
    {
        // Arrange
        var pref = new UserFieldPreference
        {
            KasaType = "Genel",
            SelectedFieldsJson = null!
        };

        // Act
        var result = pref.GetSelectedFields();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetSelectedFields_With_Invalid_Json_Returns_Empty()
    {
        // Arrange
        var pref = new UserFieldPreference
        {
            KasaType = "Genel",
            SelectedFieldsJson = "invalid json {"
        };

        // Act
        var result = pref.GetSelectedFields();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void New_Preference_Has_Default_Values()
    {
        // Act
        var pref = new UserFieldPreference();

        // Assert
        Assert.NotEqual(Guid.Empty, pref.Id);
        Assert.NotEqual(default, pref.CreatedAtUtc);
        Assert.Equal("[]", pref.SelectedFieldsJson);
    }

    [Fact]
    public void SetSelectedFields_Updates_Timestamps()
    {
        // Arrange
        var pref = new UserFieldPreference
        {
            KasaType = "Aksam"
        };
        var originalUpdated = pref.UpdatedAtUtc;

        // Wait a bit
        System.Threading.Thread.Sleep(10);

        // Act
        pref.SetSelectedFields(new List<string> { "field1" });

        // Assert
        Assert.True(pref.UpdatedAtUtc >= originalUpdated);
    }

    [Fact]
    public void Global_Preference_Has_Null_UserName()
    {
        // Arrange & Act
        var pref = new UserFieldPreference
        {
            KasaType = "Aksam",
            UserName = null
        };
        pref.SetSelectedFields(new[] { "field1", "field2" });

        // Assert
        Assert.Null(pref.UserName);
        Assert.Equal("Aksam", pref.KasaType);
        Assert.Equal(2, pref.GetSelectedFields().Count);
    }

    [Fact]
    public void KasaType_Can_Be_Set()
    {
        // Arrange
        var pref = new UserFieldPreference();

        // Act
        pref.KasaType = "Ortak";

        // Assert
        Assert.Equal("Ortak", pref.KasaType);
    }
}
