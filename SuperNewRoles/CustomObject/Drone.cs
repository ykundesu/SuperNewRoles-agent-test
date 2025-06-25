using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SuperNewRoles.Modules;
using UnityEngine;
using SuperNewRoles.CustomCosmetics;

namespace SuperNewRoles.CustomObject;

public class Drone : MonoBehaviour
{
    public static List<Drone> AllDrone = new();
    public static Dictionary<ExPlayerControl, List<Drone>> PlayerDrone = new();
    public static GameObject AllDroneObject;
    public static GameObject InstantiateDrone;

    public static Sprite[] Active =>
    [
        AssetManager.GetAsset<Sprite>("UbiquitousDroneActiveAnim1.png"),
        AssetManager.GetAsset<Sprite>("UbiquitousDroneActiveAnim2.png")
    ];
    public static Sprite Idle => AssetManager.GetAsset<Sprite>("UbiquitousDroneIdle.png");

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.CoStartGame))]
    public static class AmongUsClientCoStartGamePatch
    {
        public static void Postfix()
        {
            ClearAndReload();
        }
    }

    public static void ClearAndReload()
    {
        AllDrone = new();
        PlayerDrone = new();
    }

    public GameObject DroneObject;
    public GameObject RendererObject;
    public GameObject LightChild;
    public Rigidbody2D Body;
    public SpriteRenderer Renderer;
    public ExPlayerControl Owner;
    public bool IsActive;
    public int SpriteNumber;
    public float SpriteTimer;
    public float HoveringTimer;
    public int RemainingTurn;
    public bool UnderOperation => DestroyableSingleton<HudManager>.Instance.PlayerCam.Target == this;

    // 設定値（後でUbiquitousクラスから参照）
    public static float DroneStayTurn = 5f;
    public static float FlyingSpeed = 1.5f;
    public static float DroneVisibilityRange = 1f;

    public void Start()
    {
        DroneObject = gameObject;
        RendererObject = transform.Find("Renderer").gameObject;
        LightChild = transform.Find("LightChild").gameObject;
        Body = DroneObject.GetComponent<Rigidbody2D>() ?? DroneObject.AddComponent<Rigidbody2D>();
        Renderer = RendererObject.GetComponent<SpriteRenderer>() ?? RendererObject.AddComponent<SpriteRenderer>();
        SpriteNumber = 0;
        SpriteTimer = 0f;
        HoveringTimer = 0f;
        RemainingTurn = (int)DroneStayTurn;
    }

    private static readonly Vector3 _reusableVector = Vector3.zero;
    private static readonly Color _semiTransparentColor = new(1f, 1f, 1f, 0.5f);
    private float _hoverUpdateTimer = 0f;
    private const float HOVER_UPDATE_INTERVAL = 0.02f; // Reduce hover update frequency
    
    public void Update()
    {
        if (IsActive)
        {
            // Reduce hovering animation update frequency
            _hoverUpdateTimer += Time.deltaTime;
            if (_hoverUpdateTimer >= HOVER_UPDATE_INTERVAL)
            {
                _hoverUpdateTimer = 0f;
                // Reuse vector instead of creating new one
                _reusableVector.Set(0f, Mathf.Sin(HoveringTimer) / 20f, 0f);
                Renderer.transform.localPosition = _reusableVector;
                HoveringTimer += Time.deltaTime * 5f; // Compensate for reduced update frequency
                if (HoveringTimer > Mathf.PI * 2) HoveringTimer -= Mathf.PI * 2;
            }

            SpriteTimer += Time.deltaTime;
            if (SpriteTimer > 0.125f)
            {
                SpriteTimer = 0f;
                SpriteNumber = (SpriteNumber + 1) % 2;
                Renderer.sprite = Active[SpriteNumber];
            }

            if (Body.velocity.x > 0.1f) Renderer.flipX = true;
            else if (Body.velocity.x < -0.1f) Renderer.flipX = false;

            Renderer.color = UnderOperation ? Color.white : _semiTransparentColor;

            if (UnderOperation)
            {
                float size = ShipStatus.Instance.MaxLightRadius * DroneVisibilityRange * 5.25f;
                // Reuse vector instead of creating new one
                _reusableVector.Set(size, size, 1f);
                LightChild.transform.localScale = _reusableVector;
            }
            else
            {
                LightChild.transform.localScale = Vector3.zero;
            }
        }
        else
        {
            Renderer.sprite = Idle;
            Renderer.color = Color.white;
            Renderer.transform.localPosition = Vector3.zero;
            if (LightChild) Destroy(LightChild);
        }
    }

    public void FixedUpdate()
    {
        Vector3 position = transform.position;
        if (IsActive)
        {
            if (UnderOperation) Body.velocity = DestroyableSingleton<HudManager>.Instance.joystick.DeltaL * FlyingSpeed * Owner.MyPhysics.Speed;
            else Body.velocity = Vector2.zero;
            position.z = -1f;
        }
        else position.z = position.y / 1000f;
        transform.position = position;
    }

    public void OnDestroy()
    {
        AllDrone.Remove(this);
        if (PlayerDrone.TryGetValue(Owner, out List<Drone> drones))
            drones.Remove(this);
    }

    public void Destroy() => Destroy(gameObject);

    public static Drone CreateActiveDrone(string id, Vector2 pos, ExPlayerControl owner)
    {
        GameObject drone_object = Instantiate(InstantiateDrone, AllDroneObject.transform);
        drone_object.name = $"Drone {id}";
        drone_object.transform.position = pos;
        drone_object.SetActive(true);
        Drone drone = drone_object.AddComponent<Drone>();
        drone.IsActive = true;
        drone.Owner = owner;
        AllDrone.Add(drone);
        if (!PlayerDrone.ContainsKey(owner))
            PlayerDrone[owner] = new();
        PlayerDrone[owner].Add(drone);
        return drone;
    }

    public static Drone CreateIdleDrone(string id, Vector2 pos, ExPlayerControl owner)
    {
        Drone drone = CreateActiveDrone(id, pos, owner);
        drone.IsActive = false;
        return drone;
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.OnDestroy))]
    public static class MeetingHudOnDestroyPatch
    {
        public static void Postfix()
        {
            CloseMeeting();
        }
    }

    public static void CloseMeeting()
    {
        List<Drone> removes = new();
        foreach (Drone drone in AllDrone)
        {
            if (drone.IsActive) continue;
            if (drone.RemainingTurn-- > 0) continue;
            removes.Add(drone);
        }
        foreach (Drone drone in removes)
            drone.Destroy();
    }

    public static List<ExPlayerControl> GetPlayersVicinity(ExPlayerControl owner)
    {
        List<ExPlayerControl> vicinity = new();
        if (!PlayerDrone.ContainsKey(owner)) return vicinity;
        List<Drone> idle = PlayerDrone[owner].FindAll(x => !x.IsActive);
        foreach (ExPlayerControl player in ExPlayerControl.ExPlayerControls)
        {
            if (player.IsDead() || player.PlayerId == owner.PlayerId) continue;
            if (idle.Any(x => Vector2.Distance(x.transform.position, player.GetTruePosition()) <= ShipStatus.Instance.MaxLightRadius * DroneVisibilityRange))
                vicinity.Add(player);
        }
        return vicinity;
    }

    [HarmonyPatch(typeof(ShipStatus))]
    public static class ShipStatusPatch
    {
        [HarmonyPatch(nameof(ShipStatus.Start)), HarmonyPostfix]
        public static void StartPostfix()
        {
            AllDroneObject = new("AllDroneObject");
            AllDroneObject.transform.position = new(0f, 0f, 0f);

            InstantiateDrone = new("Instantiate Drone") { layer = LayerMask.NameToLayer("Players") };
            InstantiateDrone.transform.SetParent(AllDroneObject.transform);
            InstantiateDrone.SetActive(false);
            Rigidbody2D body = InstantiateDrone.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.sleepMode = RigidbodySleepMode2D.NeverSleep;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            CircleCollider2D collider = InstantiateDrone.AddComponent<CircleCollider2D>();
            collider.offset = new(0f, -0.2f);
            collider.radius = 0.2234f;
            collider.isTrigger = false;

            GameObject drone_renderer = new("Renderer") { layer = LayerMask.NameToLayer("Objects") };
            drone_renderer.transform.SetParent(InstantiateDrone.transform);
            drone_renderer.transform.localPosition = new();
            SpriteRenderer renderer = drone_renderer.AddComponent<SpriteRenderer>();
            renderer.color = new Color(1f, 1f, 1f, 0.5f);
            renderer.sprite = Active[0];

            GameObject light_child = new("LightChild") { layer = LayerExpansion.GetShadowLayer() };
            light_child.transform.SetParent(InstantiateDrone.transform);
            light_child.transform.localPosition = new();
            light_child.transform.localScale = Vector3.zero;
            LightSource source = PlayerControl.LocalPlayer.LightPrefab;
            light_child.AddComponent<MeshFilter>().mesh = source.lightChildMesh;
            light_child.AddComponent<MeshRenderer>().material.shader = source.LightCutawayMaterial.shader;
        }
    }
}