using System.Collections;
using UnityEngine;

public class TowerActivation : MonoBehaviour
{
    // Renamed from `light`. That name shadowed the old Component.light property and reads as a
    // built-in rather than a field, which is exactly the sort of thing that makes a later
    // reader hesitate over a line that is doing nothing clever.
    private Light towerLight;
    private Animator animator;
    public bool isActivated = false;
    public portal Portal;

    /*
     * The activation "feel", which is the half of the professor's note that was not a bug:
     * "The animation is not looping correctly and it's a bit underwhelming."
     *
     * The loop was a single unchecked box on the clip (fixed separately). The underwhelming
     * part was this script: activating a tower snapped the light to green on one frame and
     * that was the entire reward for crossing a hostile map to reach it.
     *
     * A tower now ramps up instead. The light surges past its final brightness and settles
     * back -- a flat fade reads as a UI element changing state, whereas an overshoot reads as
     * something powering on -- and then holds a slow pulse so an activated tower is legible
     * from across the level. All driven from the original colour and intensity, so a tower
     * that was dimmed or tinted in the scene keeps its own character.
     */
    private static readonly Color ActivatedColour = new Color(0.24f, 0.95f, 0.55f);
    private const float RampSeconds = 1.1f;
    private const float OvershootMultiplier = 2.4f;
    private const float PulseAmplitude = 0.12f;
    private const float PulseSpeed = 1.7f;

    private Color idleColour;
    private float baseIntensity;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        towerLight = GetComponentInChildren<Light>();
        animator = GetComponent<Animator>();

        if (towerLight != null)
        {
            idleColour = towerLight.color;
            baseIntensity = towerLight.intensity;
        }
    }

    // The empty Update() was removed: Unity calls the message on every tower, every frame, to
    // do nothing.

    public void Activate()
    {
        if (isActivated)
        {
            return;
        }
        isActivated = true;
        animator.SetBool("Activated", true);

        if (towerLight != null)
        {
            StopAllCoroutines();
            StartCoroutine(PowerUp());
        }
        else
        {
            // Guarded because the original assumed a Light in the children and would have
            // thrown on any tower prefab that lost one -- taking the portal progression with
            // it, since Portal.TowerWasActivated() is what unlocks the ending.
            Debug.LogWarning($"[TowerActivation] {name} has no Light in its children.", this);
        }

        Portal.TowerWasActivated();
    }

    private IEnumerator PowerUp()
    {
        var elapsed = 0f;
        var peak = baseIntensity * OvershootMultiplier;

        while (elapsed < RampSeconds)
        {
            elapsed += Time.deltaTime;
            var t = Mathf.Clamp01(elapsed / RampSeconds);

            towerLight.color = Color.Lerp(idleColour, ActivatedColour, Mathf.SmoothStep(0f, 1f, t));

            // Rises fast, falls back slowly: Sin(pi * t) peaks at the halfway point, which is
            // what gives the surge-then-settle shape rather than a linear fade.
            towerLight.intensity = Mathf.Lerp(baseIntensity, peak, Mathf.Sin(Mathf.PI * t));

            yield return null;
        }

        towerLight.color = ActivatedColour;

        // Then hold, breathing gently, for as long as the tower is up.
        while (true)
        {
            towerLight.intensity = baseIntensity * (1f + PulseAmplitude * Mathf.Sin(Time.time * PulseSpeed));
            yield return null;
        }
    }
}
