using Avalonia.Media.Imaging;

namespace Samaat.App.Features.Overview;

/// <summary>One piece of a split device photo (left bud, right bud or case) with the battery figure it shows.</summary>
public sealed record ArtworkPartViewModel(BatteryFigureViewModel Figure, Bitmap Image);
