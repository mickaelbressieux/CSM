using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The animated main-menu background: a still curling house with stones being thrown at it.
///
/// It plays an end on a loop. A few stones are delivered one at a time, each sliding in from
/// the far edge, losing speed and coming to rest near the button - knocking each other around
/// on the way. Then an oversized takeout stone comes through and clears the sheet, and the
/// end starts again.
///
/// The physics is a deliberately small 2D approximation done in canvas space: exponential
/// drag, and elastic impulses between circles. It is background art, not the game - the real
/// round is Unity physics in the curling scene. Keeping it self-contained means the whole
/// backdrop is one GameObject with no image assets and nothing to wire.
///
/// Put this on a RectTransform stretched to fill the Canvas, as the first child so it draws
/// behind the menu. Every Image it creates has raycastTarget off, so it can never swallow a
/// button click.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class IceBackdrop : MonoBehaviour
{
    /// <summary>One concentric circle of the house, outermost first.</summary>
    [Serializable]
    public struct HouseRing
    {
        public Color color;
        [Tooltip("Diameter in canvas units.")]
        public float diameter;
    }

    [Header("Sheet")]
    [SerializeField] private Color iceColor = new Color(0.08f, 0.13f, 0.20f);

    [Header("House")]
    [Tooltip("Concentric circles, outermost first. Standard house: blue, white, red, white button.")]
    [SerializeField]
    private List<HouseRing> rings = new List<HouseRing>
    {
        new HouseRing { color = new Color(0.16f, 0.42f, 0.72f), diameter = 780f },
        new HouseRing { color = new Color(0.86f, 0.92f, 0.97f), diameter = 560f },
        new HouseRing { color = new Color(0.78f, 0.20f, 0.24f), diameter = 340f },
        new HouseRing { color = new Color(0.90f, 0.95f, 0.99f), diameter = 130f }
    };

    [Tooltip("Where the house sits relative to the screen centre. Offset it so stones settle " +
             "clear of the menu buttons; stones then enter from the opposite edge.")]
    [SerializeField] private Vector2 houseOffset = new Vector2(380f, 0f);
    [SerializeField, Range(0f, 1f)] private float houseOpacity = 0.35f;

    [Header("The end")]
    [Tooltip("Stones delivered before the takeout stone clears the sheet.")]
    [SerializeField, Range(1, 8)] private int throwsBeforeTakeout = 3;
    [SerializeField] private float secondsBetweenThrows = 0.6f;
    [SerializeField] private float secondsAfterTakeout = 1.2f;
    [Tooltip("Safety net: stop waiting for a throw to settle after this long.")]
    [SerializeField] private float maxSecondsPerThrow = 8f;

    [Header("Stones")]
    [SerializeField] private float stoneDiameter = 96f;
    // Its radius has to out-reach how far a resting stone can sit off the takeout's line, or
    // it sails past the outliers: at aimSpread 190 and a 48-unit stone radius, contact needs
    // a takeout radius of at least ~142. 4x diameter gives 192, so there is slack for stones
    // that collisions have nudged even wider.
    [Tooltip("How much bigger the takeout stone is than a normal one. Raise it if stones at " +
             "the edge of the house survive the takeout.")]
    [SerializeField, Range(1.5f, 7f)] private float takeoutSizeMultiplier = 4f;
    [Tooltip("How far off the button a delivered stone aims, in canvas units.")]
    [SerializeField] private float aimSpread = 190f;

    [Header("Physics")]
    [Tooltip("Speed lost per second, as a rate. Higher stops stones sooner.")]
    [SerializeField, Range(0.2f, 6f)] private float drag = 1.5f;
    [Tooltip("Bounciness of stone-on-stone contact. 1 is perfectly elastic.")]
    [SerializeField, Range(0f, 1f)] private float restitution = 0.9f;
    [Tooltip("Below this speed a stone is considered stopped.")]
    [SerializeField] private float stopSpeed = 12f;

    [Header("Colours")]
    [SerializeField] private Color stoneBodyColor = new Color(0.20f, 0.23f, 0.28f);
    [SerializeField] private Color[] stoneHandleColors =
    {
        new Color(0.82f, 0.24f, 0.27f),
        new Color(0.94f, 0.78f, 0.29f)
    };
    [SerializeField] private Color takeoutHandleColor = new Color(0.98f, 0.82f, 0.15f);

    /// <summary>A stone in flight. Position is kept here rather than read back from the
    /// RectTransform, so the simulation is not fighting the layout for ownership of it.</summary>
    private class MenuStone
    {
        public RectTransform Rect;
        public Vector2 Position;
        public Vector2 Velocity;
        public float Radius;
        public float Mass;

        public bool IsStopped => Velocity.sqrMagnitude <= 0f;
    }

    // Sub-stepping keeps the fast takeout stone from stepping straight past a stone it should
    // have hit. Cheap here: there are only ever a handful of stones.
    private const int SubSteps = 4;

    private RectTransform selfRect;
    private RectTransform stonesRoot;
    private readonly List<MenuStone> stones = new List<MenuStone>();

    private void Awake()
    {
        selfRect = (RectTransform)transform;

        CreateIceSheet();
        CreateHouse();
        stonesRoot = CreateChild("Stones", selfRect);
        Stretch(stonesRoot);

        StartCoroutine(PlayEnds());
    }

    private void Update()
    {
        // A long frame (a scene load, a stall) must not teleport stones through each other.
        float frame = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        float step = frame / SubSteps;

        for (int i = 0; i < SubSteps; i++)
        {
            Integrate(step);
            ResolveCollisions();
        }

        RemoveStonesOffSheet();
        ApplyPositions();
    }

    // ------------------------------------------------------------------
    // The end: deliver a few stones, then clear the sheet
    // ------------------------------------------------------------------

    private IEnumerator PlayEnds()
    {
        while (true)
        {
            ClearStones();

            for (int i = 0; i < throwsBeforeTakeout; i++)
            {
                Deliver(isTakeout: false, handleColor: HandleColor(i));
                yield return WaitForSheetToSettle();
                yield return new WaitForSecondsRealtime(secondsBetweenThrows);
            }

            Deliver(isTakeout: true, handleColor: takeoutHandleColor);
            yield return WaitForSheetToSettle();
            yield return new WaitForSecondsRealtime(secondsAfterTakeout);
        }
    }

    /// <summary>Waits until nothing is moving any more, with a timeout so a stone wedged
    /// against another can never stall the loop.</summary>
    private IEnumerator WaitForSheetToSettle()
    {
        float elapsed = 0f;
        while (elapsed < maxSecondsPerThrow && !SheetIsSettled())
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private bool SheetIsSettled()
    {
        foreach (MenuStone stone in stones)
        {
            if (!stone.IsStopped)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Throw a stone from the edge furthest from the house, aimed at the button.
    ///
    /// Under exponential drag a stone travels v0/drag before stopping, so choosing the launch
    /// speed from the distance we want it to cover is exact - that is what makes them settle
    /// around the house instead of anywhere. The takeout stone is simply aimed far past the
    /// house, so it never stops and carries everything out with it.
    /// </summary>
    private void Deliver(bool isTakeout, Color handleColor)
    {
        float diameter = isTakeout ? stoneDiameter * takeoutSizeMultiplier : stoneDiameter;
        float radius = diameter * 0.5f;
        float halfWidth = selfRect.rect.width * 0.5f;

        // Enter from whichever side gives the longer run at the house.
        float entryX = houseOffset.x >= 0f ? -halfWidth - radius : halfWidth + radius;

        Vector2 target = houseOffset;
        if (!isTakeout)
        {
            // Spread the resting places around the button so the house fills up believably.
            target += new Vector2(
                UnityEngine.Random.Range(-aimSpread, aimSpread),
                UnityEngine.Random.Range(-aimSpread, aimSpread));
        }

        Vector2 start = new Vector2(
            entryX,
            houseOffset.y + UnityEngine.Random.Range(-aimSpread, aimSpread) * 0.5f);

        Vector2 toTarget = target - start;
        // Past the far edge, so the takeout stone is still moving when it leaves.
        float distance = isTakeout ? selfRect.rect.width * 1.8f : toTarget.magnitude;

        MenuStone stone = CreateStone(diameter, handleColor);
        stone.Position = start;
        stone.Velocity = toTarget.normalized * (distance * drag);

        stones.Add(stone);
    }

    // ------------------------------------------------------------------
    // Simulation
    // ------------------------------------------------------------------

    private void Integrate(float dt)
    {
        // Exponential decay rather than a constant deceleration: it never overshoots into
        // negative speed, whatever the frame length.
        float damping = Mathf.Exp(-drag * dt);

        foreach (MenuStone stone in stones)
        {
            stone.Position += stone.Velocity * dt;
            stone.Velocity *= damping;

            if (stone.Velocity.sqrMagnitude < stopSpeed * stopSpeed)
            {
                stone.Velocity = Vector2.zero;
            }
        }
    }

    private void ResolveCollisions()
    {
        for (int i = 0; i < stones.Count; i++)
        {
            for (int j = i + 1; j < stones.Count; j++)
            {
                ResolvePair(stones[i], stones[j]);
            }
        }
    }

    private void ResolvePair(MenuStone a, MenuStone b)
    {
        Vector2 delta = b.Position - a.Position;
        float contactDistance = a.Radius + b.Radius;
        float sqrDistance = delta.sqrMagnitude;

        if (sqrDistance >= contactDistance * contactDistance || sqrDistance <= Mathf.Epsilon)
        {
            return;
        }

        float distance = Mathf.Sqrt(sqrDistance);
        Vector2 normal = delta / distance;

        float invMassA = 1f / a.Mass;
        float invMassB = 1f / b.Mass;
        float invMassSum = invMassA + invMassB;

        // Push them apart first, share by inverse mass, so the heavy takeout stone barely
        // yields and a resting stone gets shoved clear instead of sinking into it.
        Vector2 correction = normal * ((contactDistance - distance) / invMassSum);
        a.Position -= correction * invMassA;
        b.Position += correction * invMassB;

        float separatingSpeed = Vector2.Dot(b.Velocity - a.Velocity, normal);
        if (separatingSpeed > 0f)
        {
            return;
        }

        float impulse = -(1f + restitution) * separatingSpeed / invMassSum;
        a.Velocity -= normal * (impulse * invMassA);
        b.Velocity += normal * (impulse * invMassB);
    }

    private void RemoveStonesOffSheet()
    {
        float halfWidth = selfRect.rect.width * 0.5f;
        float halfHeight = selfRect.rect.height * 0.5f;

        for (int i = stones.Count - 1; i >= 0; i--)
        {
            MenuStone stone = stones[i];
            // Generous margin: a stone still on its way in starts outside the sheet.
            float marginX = halfWidth + stone.Radius * 3f;
            float marginY = halfHeight + stone.Radius * 3f;

            if (Mathf.Abs(stone.Position.x) > marginX || Mathf.Abs(stone.Position.y) > marginY)
            {
                Destroy(stone.Rect.gameObject);
                stones.RemoveAt(i);
            }
        }
    }

    private void ApplyPositions()
    {
        foreach (MenuStone stone in stones)
        {
            stone.Rect.anchoredPosition = stone.Position;
        }
    }

    private void ClearStones()
    {
        foreach (MenuStone stone in stones)
        {
            if (stone.Rect != null)
            {
                Destroy(stone.Rect.gameObject);
            }
        }

        stones.Clear();
    }

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    private void CreateIceSheet()
    {
        RectTransform sheet = CreateChild("Ice", selfRect);
        Stretch(sheet);
        AddImage(sheet, iceColor, sprite: null);
    }

    private void CreateHouse()
    {
        RectTransform root = CreateChild("House", selfRect);
        Centre(root);
        root.anchoredPosition = houseOffset;

        foreach (HouseRing ring in rings)
        {
            if (ring.diameter <= 0f)
            {
                continue;
            }

            RectTransform ringRect = CreateChild($"Ring_{ring.diameter:F0}", root);
            Centre(ringRect);
            ringRect.sizeDelta = new Vector2(ring.diameter, ring.diameter);

            Color color = ring.color;
            color.a *= houseOpacity;
            AddImage(ringRect, color, UISpriteFactory.Circle());
        }
    }

    private MenuStone CreateStone(float diameter, Color handleColor)
    {
        Sprite circle = UISpriteFactory.Circle();

        RectTransform rect = CreateChild("Stone", stonesRoot);
        Centre(rect);
        rect.sizeDelta = new Vector2(diameter, diameter);
        AddImage(rect, stoneBodyColor, circle);

        RectTransform handle = CreateChild("Handle", rect);
        Centre(handle);
        handle.sizeDelta = new Vector2(diameter * 0.34f, diameter * 0.34f);
        AddImage(handle, handleColor, circle);

        float radius = diameter * 0.5f;
        return new MenuStone
        {
            Rect = rect,
            Radius = radius,
            // Mass by area, so the takeout stone hits with the weight its size implies.
            Mass = radius * radius
        };
    }

    private Color HandleColor(int index)
    {
        if (stoneHandleColors == null || stoneHandleColors.Length == 0)
        {
            return Color.white;
        }

        return stoneHandleColors[index % stoneHandleColors.Length];
    }

    private static RectTransform CreateChild(string name, RectTransform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    // A null sprite gives a plain filled rect, which is what the ice sheet wants.
    private static void AddImage(RectTransform target, Color color, Sprite sprite)
    {
        Image image = target.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        // Backdrop art must never intercept clicks meant for the menu buttons.
        image.raycastTarget = false;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void Centre(RectTransform rect)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
    }
}
