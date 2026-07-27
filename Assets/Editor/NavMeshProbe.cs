// Answers one question: is there a usable NavMesh in this scene, and where.
//
// The WebGL build logs "Failed to create agent because there is no valid NavMesh" five times
// -- once per robot -- which means every enemy in the shipped game stands still. The scene
// looks correct: the NavMesh Surface object is active, its component is enabled, and its
// m_NavMeshData guid matches the baked asset's .meta exactly. A retry in Start() did not
// recover it, and neither did NavMesh.SamplePosition, which is the interesting part: sampling
// fails when there is no mesh AT ALL, not merely when an agent is standing off the edge of one.
//
// So the question is no longer "why did the agents miss it" but "is it there". This reports
// what the runtime navigation system can actually see, rather than what the inspector shows.
//
// Run from Tools > Echoes > 6. Probe the NavMesh.

using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

public static class NavMeshProbe
{
    [MenuItem("Tools/Echoes/6. Probe the NavMesh", priority = 6)]
    public static void Probe()
    {
        if (!EnsureSceneOpen()) return;

        // What the navigation system holds right now. In the editor a NavMeshSurface registers
        // its data in OnEnable the same way it does in a player, so this is a fair comparison
        // with the build -- if the triangulation is empty here too, the bake is the problem
        // rather than anything about how the build was made.
        var triangulation = NavMesh.CalculateTriangulation();
        Debug.Log($"[NavMeshProbe] runtime NavMesh: {triangulation.vertices.Length} vertices, " +
                  $"{triangulation.indices.Length / 3} triangles, {triangulation.areas.Length} areas");

        foreach (var surface in Object.FindObjectsByType<NavMeshSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var data = surface.navMeshData;
            Debug.Log($"[NavMeshProbe] surface '{surface.name}': " +
                      $"activeInHierarchy={surface.gameObject.activeInHierarchy}, enabled={surface.enabled}, " +
                      $"data={(data == null ? "NULL" : data.name)}, " +
                      $"bounds={(data == null ? "-" : data.sourceBounds.ToString())}, " +
                      $"agentTypeID={surface.agentTypeID}, collectObjects={surface.collectObjects}, " +
                      $"layerMask={surface.layerMask.value}");
        }

        // Every agent, and whether the mesh reaches where it is standing. An agent whose
        // position samples cleanly but which still reports "no valid NavMesh" is an ordering
        // problem; one that does not sample is either off the mesh or there is no mesh.
        foreach (var agent in Object.FindObjectsByType<NavMeshAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var position = agent.transform.position;
            var sampled = NavMesh.SamplePosition(position, out NavMeshHit hit, 10f, NavMesh.AllAreas);
            Debug.Log($"[NavMeshProbe] agent '{agent.name}' at {position}: " +
                      $"agentTypeID={agent.agentTypeID}, " +
                      $"sampleWithin10m={(sampled ? $"yes, {Vector3.Distance(position, hit.position):0.00}m away" : "NO")}");
        }

        // The agent type IDs have to match between the surface and the agents. A surface baked
        // for "Humanoid" gives an agent set to a different type nothing to walk on, and the
        // failure message is the same one as having no mesh at all -- which is exactly the sort
        // of thing that survives a visual inspection of the scene.
        var settingsCount = NavMesh.GetSettingsCount();
        for (var i = 0; i < settingsCount; i++)
        {
            var settings = NavMesh.GetSettingsByIndex(i);
            Debug.Log($"[NavMeshProbe] agent type {i}: id={settings.agentTypeID}, " +
                      $"name='{NavMesh.GetSettingsNameFromID(settings.agentTypeID)}', " +
                      $"radius={settings.agentRadius}, height={settings.agentHeight}");
        }

        var robotTypes = Object.FindObjectsByType<NavMeshAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Select(a => a.agentTypeID).Distinct().ToArray();
        var surfaceTypes = Object.FindObjectsByType<NavMeshSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Select(s => s.agentTypeID).Distinct().ToArray();

        var orphaned = robotTypes.Where(t => !surfaceTypes.Contains(t)).ToArray();
        if (orphaned.Length > 0)
        {
            Debug.LogError($"[NavMeshProbe] agent type(s) {string.Join(", ", orphaned)} have no surface baked for them. " +
                           "This produces exactly the same 'no valid NavMesh' error as having no bake at all.");
        }
    }

    static bool EnsureSceneOpen()
    {
        var active = EditorSceneManager.GetActiveScene();
        if (active.IsValid() && active.isLoaded && !string.IsNullOrEmpty(active.path)) return true;

        foreach (var entry in EditorBuildSettings.scenes)
        {
            if (!entry.enabled) continue;
            EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Single);
            return true;
        }

        Debug.LogError("[NavMeshProbe] no enabled scene in Build Settings to open.");
        return false;
    }
}
