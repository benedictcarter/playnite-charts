using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using ImportExclusionItem = Playnite.ImportExclusionItem;

namespace PlayniteCharts.DevHarness
{
    /// <summary>
    /// The bits of the database the Game model's navigation properties actually
    /// touch (Sources[id], CompletionStatuses[id], Genres.Get(ids), ...). Everything
    /// else throws - this exists only to let the harness build real Game objects.
    /// </summary>
    internal class FakeCollection<TItem> : IItemCollection<TItem> where TItem : DatabaseObject
    {
        private readonly Dictionary<Guid, TItem> items = new Dictionary<Guid, TItem>();

        public GameDatabaseCollection CollectionType => GameDatabaseCollection.Uknown;
        public int Count => items.Count;
        public bool IsReadOnly => false;

        public TItem this[Guid id]
        {
            get => items.TryGetValue(id, out var v) ? v : null;
            set => items[id] = value;
        }

        public TItem Get(Guid id) => this[id];
        public List<TItem> Get(IList<Guid> ids) => ids?.Select(Get).Where(i => i != null).ToList() ?? new List<TItem>();
        public bool ContainsItem(Guid id) => items.ContainsKey(id);
        public void Add(TItem item) => items[item.Id] = item;
        public void Add(IEnumerable<TItem> newItems) { foreach (var i in newItems) Add(i); }
        public bool Contains(TItem item) => item != null && items.ContainsKey(item.Id);
        public void Clear() => items.Clear();
        public void CopyTo(TItem[] array, int index) => items.Values.CopyTo(array, index);
        public bool Remove(TItem item) => item != null && items.Remove(item.Id);
        public bool Remove(Guid id) => items.Remove(id);
        public bool Remove(IEnumerable<TItem> toRemove) { foreach (var i in toRemove) Remove(i); return true; }
        public IEnumerator<TItem> GetEnumerator() => items.Values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public IEnumerable<TItem> GetClone() => items.Values.ToList();
        public void BeginBufferUpdate() { }
        public void EndBufferUpdate() { }
        public void Update(TItem item) => Add(item);
        public void Update(IEnumerable<TItem> updated) => Add(updated);
        public void Dispose() { }

        public TItem Add(string itemName) => throw new NotSupportedException();
        public TItem Add(string itemName, Func<TItem, string, bool> existingComparer) => throw new NotSupportedException();
        public IEnumerable<TItem> Add(List<string> names) => throw new NotSupportedException();
        public IEnumerable<TItem> Add(List<string> names, Func<TItem, string, bool> existingComparer) => throw new NotSupportedException();
        public IEnumerable<TItem> Add(IEnumerable<MetadataProperty> properties) => throw new NotSupportedException();
        public TItem Add(MetadataProperty property) => throw new NotSupportedException();
        public IDisposable BufferedUpdate() => new NoopScope();

        private class NoopScope : IDisposable
        {
            public void Dispose() { }
        }

#pragma warning disable CS0067
        public event EventHandler<ItemCollectionChangedEventArgs<TItem>> ItemCollectionChanged;
        public event EventHandler<ItemUpdatedEventArgs<TItem>> ItemUpdated;
#pragma warning restore CS0067
    }

    internal class FakeDatabase : IGameDatabase
    {
        public IItemCollection<Game> Games { get; } = new FakeCollection<Game>();
        public IItemCollection<Platform> Platforms { get; } = new FakeCollection<Platform>();
        public IItemCollection<Emulator> Emulators { get; } = new FakeCollection<Emulator>();
        public IItemCollection<Genre> Genres { get; } = new FakeCollection<Genre>();
        public IItemCollection<Company> Companies { get; } = new FakeCollection<Company>();
        public IItemCollection<Tag> Tags { get; } = new FakeCollection<Tag>();
        public IItemCollection<Category> Categories { get; } = new FakeCollection<Category>();
        public IItemCollection<Series> Series { get; } = new FakeCollection<Series>();
        public IItemCollection<AgeRating> AgeRatings { get; } = new FakeCollection<AgeRating>();
        public IItemCollection<Region> Regions { get; } = new FakeCollection<Region>();
        public IItemCollection<GameSource> Sources { get; } = new FakeCollection<GameSource>();
        public IItemCollection<GameFeature> Features { get; } = new FakeCollection<GameFeature>();
        public IItemCollection<GameScannerConfig> GameScanners { get; } = new FakeCollection<GameScannerConfig>();
        public IItemCollection<CompletionStatus> CompletionStatuses { get; } = new FakeCollection<CompletionStatus>();
        public IItemCollection<ImportExclusionItem> ImportExclusions { get; } = new FakeCollection<ImportExclusionItem>();
        public IItemCollection<FilterPreset> FilterPresets { get; } = new FakeCollection<FilterPreset>();

        public bool IsOpen => true;

#pragma warning disable CS0067
        public event EventHandler DatabaseOpened;
#pragma warning restore CS0067

        public Game ImportGame(GameMetadata game) => throw new NotSupportedException();
        public Game ImportGame(GameMetadata game, LibraryPlugin sourcePlugin) => throw new NotSupportedException();
        public bool GetGameMatchesFilter(Game game, FilterPresetSettings filterSettings) => true;
        public bool GetGameMatchesFilter(Game game, FilterPresetSettings filterSettings, bool useFuzzyNameMatch) => true;
        public IEnumerable<Game> GetFilteredGames(FilterPresetSettings filterSettings) => Games;
        public IEnumerable<Game> GetFilteredGames(FilterPresetSettings filterSettings, bool useFuzzyNameMatch) => Games;
    }
}
