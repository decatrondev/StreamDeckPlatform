using Deck.Core.Data;
using Deck.Core.Model;
using Microsoft.EntityFrameworkCore;

namespace Deck.Core.Tests;

public class DeckDbContextTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"deck-test-{Guid.NewGuid()}.db");

    [Fact]
    public async Task Profile_Page_ButtonSlot_ActionStep_RoundTrip()
    {
        var options = DeckDb.CreateOptions(_dbPath);

        var pageId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var buttonId = Guid.NewGuid();

        await using (var db = new DeckDbContext(options))
        {
            DeckDb.EnsureMigrated(db);

            db.Pages.Add(new Page { Id = pageId, Name = "Principal", Rows = 3, Columns = 5 });
            db.Profiles.Add(new Profile { Id = profileId, Name = "Streaming", RootPageId = pageId });
            db.ButtonSlots.Add(new ButtonSlot
            {
                Id = buttonId,
                PageId = pageId,
                Row = 0,
                Column = 0,
                Type = ButtonSlotType.Action,
                Label = "Mutear"
            });
            db.ActionSteps.Add(new ActionStep
            {
                Id = Guid.NewGuid(),
                ButtonSlotId = buttonId,
                Order = 0,
                PluginId = "obs",
                ActionId = "toggle-mute",
                ParametersJson = """{"source":"Mic"}"""
            });

            await db.SaveChangesAsync();
        }

        await using (var db = new DeckDbContext(options))
        {
            var profile = await db.Profiles.SingleAsync(p => p.Id == profileId);
            var button = await db.ButtonSlots.SingleAsync(b => b.Id == buttonId);
            var steps = await db.ActionSteps.Where(a => a.ButtonSlotId == buttonId).ToListAsync();

            Assert.Equal("Streaming", profile.Name);
            Assert.Equal(pageId, profile.RootPageId);
            Assert.Equal(ButtonSlotType.Action, button.Type);
            Assert.Single(steps);
            Assert.Equal("obs", steps[0].PluginId);
        }
    }

    [Fact]
    public async Task ButtonSlot_Folder_PointsToAnotherPage()
    {
        var options = DeckDb.CreateOptions(_dbPath);
        var rootPageId = Guid.NewGuid();
        var subPageId = Guid.NewGuid();
        var folderButtonId = Guid.NewGuid();

        await using (var db = new DeckDbContext(options))
        {
            DeckDb.EnsureMigrated(db);

            db.Pages.Add(new Page { Id = rootPageId, Name = "Raíz", Rows = 3, Columns = 5 });
            db.Pages.Add(new Page { Id = subPageId, Name = "Carpeta OBS", Rows = 3, Columns = 5 });
            db.ButtonSlots.Add(new ButtonSlot
            {
                Id = folderButtonId,
                PageId = rootPageId,
                Row = 0,
                Column = 4,
                Type = ButtonSlotType.Folder,
                TargetPageId = subPageId,
                Label = "OBS"
            });

            await db.SaveChangesAsync();
        }

        await using (var db = new DeckDbContext(options))
        {
            var folderButton = await db.ButtonSlots.SingleAsync(b => b.Id == folderButtonId);
            Assert.Equal(ButtonSlotType.Folder, folderButton.Type);
            Assert.Equal(subPageId, folderButton.TargetPageId);
        }
    }

    public void Dispose() => File.Delete(_dbPath);
}
