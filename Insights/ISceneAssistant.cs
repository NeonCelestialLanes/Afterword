using System;
using System.Threading;
using System.Threading.Tasks;

namespace RPChronicles.Insights;

/// <summary>
/// A hook for a later assistant that can summarize a scene or notice story details.
/// Nothing in this version implements it, and the recorder never calls it.
/// A future version has to ask before any scene text leaves the computer.
/// </summary>
public interface ISceneAssistant
{
    string Name { get; }

    Task<string> SummarizeSceneAsync(Guid sceneId, string transcript, CancellationToken cancellationToken);
}
