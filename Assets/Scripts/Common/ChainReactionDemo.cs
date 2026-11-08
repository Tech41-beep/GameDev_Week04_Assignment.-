using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PhysicsDemo
{
    /// <summary>
    /// Chain-reaction demo across three stations of the Starter Sandbox:
    ///   1. Ball rolls   (Station A) - Rigidbody sphere rolls down the ramp and hits the wall
    ///   2. Cube falls   (Station B) - the wall hit releases the ghost cube (Rigidbody + BoxCollider added here)
    ///   3. Flag waves   (Station E) - the cube landing switches the wind on for the flag (Cloth + ClothPinner added here)
    /// Everything is added from code at scene start, so the scene objects stay plain. Enter = start / restart.
    /// </summary>
    [DefaultExecutionOrder(20)]
    public class ChainReactionDemo : MonoBehaviour
    {
        public bool autoStart = true;
        public float autoStartDelay = 2f;
        [Tooltip("Move the camera to each station as the chain reaches it.")]
        public bool cameraFollows = true;
        [Tooltip("If a step does not trigger the next one within this time, move on anyway.")]
        public float failSafeSeconds = 10f;

        enum Step { Waiting, BallRolling, CubeFalling, FlagWaving }

        Step step = Step.Waiting;
        float stepTime;
        float startTimer;

        Transform ball, ramp, wall, cube, flag;
        Rigidbody ballBody, cubeBody;
        Cloth cloth;
        OrbitCamera orbit;
        bool[] done = new bool[3];

        void Awake()
        {
            ball = Find("Sphere");
            ramp = Find("Ramp");
            wall = Find("Wall");
            cube = Find("Ghost cube (no collider)");
            flag = Find("Flag");
            orbit = FindFirstObjectByType<OrbitCamera>();

            SetupBall();
            SetupCube();
            SetupFlag();
        }

        void Start()
        {
            startTimer = autoStartDelay;
            var hud = DemoHUD.Instance;
            if (hud == null) return;
            hud.AddHeader("Chain reaction");
            hud.AddToggle("Camera follows the chain", cameraFollows, v => cameraFollows = v);
            hud.AddButton("Start / restart (Enter)", Restart);
            hud.AddLiveSource(StatusText);
        }

        static Transform Find(string objectName)
        {
            var go = GameObject.Find(objectName);
            if (go == null) Debug.LogError($"ChainReactionDemo: object '{objectName}' not found in the scene.");
            return go != null ? go.transform : null;
        }

        // ---- setup ---------------------------------------------------------------------------------------------

        void SetupBall()
        {
            if (ball == null) return;
            ballBody = ball.gameObject.AddComponent<Rigidbody>();
            ballBody.mass = 1f;
            ballBody.isKinematic = true; // held at the top of the ramp until the chain starts

            if (ramp != null)
            {
                // Top end of the ramp (local x = -0.5 of the unit cube mesh), resting on the surface.
                float radius = 0.5f * ball.lossyScale.x;
                Vector3 surface = ramp.TransformPoint(new Vector3(-0.4f, 0.5f, 0f));
                ball.position = surface + ramp.up * (radius + 0.02f);
            }
            SetLabel(ball, "1. Ball rolls");

            if (wall != null) wall.gameObject.AddComponent<CollisionRelay>().Init(ball.gameObject, OnBallHitWall);
        }

        void SetupCube()
        {
            if (cube == null) return;
            if (cube.GetComponent<Collider>() == null) cube.gameObject.AddComponent<BoxCollider>();
            cubeBody = cube.gameObject.AddComponent<Rigidbody>();
            cubeBody.isKinematic = true; // hangs in the air until the ball hits the wall
            SetLabel(cube, "2. Cube falls");
            cube.gameObject.AddComponent<CollisionRelay>().Init(null, OnCubeLanded);
        }

        void SetupFlag()
        {
            if (flag == null) return;
            cloth = flag.GetComponent<Cloth>();
            if (cloth == null) cloth = flag.gameObject.AddComponent<Cloth>();
            if (flag.GetComponent<ClothPinner>() == null)
                flag.gameObject.AddComponent<ClothPinner>().pinMode = ClothPinner.PinMode.MinXEdge;
            cloth.useGravity = true;
            cloth.damping = 0.05f;
            cloth.stretchingStiffness = 1f;
            cloth.bendingStiffness = 0.2f;
            cloth.externalAcceleration = Vector3.zero;
            cloth.randomAcceleration = Vector3.zero;
            SetLabel(flag, "3. Flag waves");
        }

        static void SetLabel(Transform t, string text)
        {
            var label = t.GetComponent<WorldLabel>();
            if (label != null) label.Text = text;
        }

        // ---- chain ---------------------------------------------------------------------------------------------

        void Update()
        {
            if (InputUtil.KeyDown(Key.Enter) || InputUtil.KeyDown(Key.NumpadEnter)) Restart();

            if (step == Step.Waiting)
            {
                if (autoStart && startTimer > 0f)
                {
                    startTimer -= Time.deltaTime;
                    if (startTimer <= 0f) BeginChain();
                }
                return;
            }

            // Fail-safe: never leave the chain stuck.
            if (Time.time - stepTime > failSafeSeconds)
            {
                if (step == Step.BallRolling) OnBallHitWall();
                else if (step == Step.CubeFalling) OnCubeLanded();
            }

            if (step == Step.FlagWaving && cloth != null)
            {
                // Gusty wind: a steady push plus a slowly changing direction.
                float t = Time.time;
                float gust = 0.6f + 0.4f * Mathf.PerlinNoise(t * 0.8f, 0.3f);
                float swirl = Mathf.PerlinNoise(0.7f, t * 0.6f) * 2f - 1f;
                cloth.externalAcceleration = new Vector3(6f * gust, 0f, (10f + 6f * swirl) * gust);
                cloth.randomAcceleration = new Vector3(10f, 4f, 18f) * gust;
            }
        }

        void Restart() => SceneLoader.Reload();

        void BeginChain()
        {
            if (ballBody == null) return;
            step = Step.BallRolling;
            stepTime = Time.time;
            ballBody.isKinematic = false;
            ballBody.WakeUp();
            Focus(ball.position, 12f);
        }

        void OnBallHitWall()
        {
            if (step != Step.BallRolling) return;
            done[0] = true;
            step = Step.CubeFalling;
            stepTime = Time.time;
            if (cubeBody != null)
            {
                cubeBody.isKinematic = false;
                cubeBody.AddTorque(new Vector3(1f, 0.5f, 0.3f), ForceMode.Impulse); // a small tumble
                cubeBody.WakeUp();
            }
            if (cube != null) Focus(cube.position + Vector3.down * 1.5f, 12f);
        }

        void OnCubeLanded()
        {
            if (step != Step.CubeFalling) return;
            done[1] = true;
            step = Step.FlagWaving;
            stepTime = Time.time;
            done[2] = true;
            if (flag != null) Focus(flag.position + new Vector3(2f, -2f, 0f), 14f);
        }

        void Focus(Vector3 point, float distance)
        {
            if (cameraFollows && orbit != null) orbit.FocusOn(point, distance);
        }

        string StatusText()
        {
            string Mark(int i) => done[i] ? "[x]" : "[ ]";
            string state = step == Step.Waiting
                ? (autoStart && startTimer > 0f ? $"Starting in {startTimer:0.0}s" : "Press Enter to start")
                : step.ToString();
            return $"{Mark(0)} 1. Ball rolls  (Station A)\n{Mark(1)} 2. Cube falls  (Station B)\n{Mark(2)} 3. Flag waves  (Station E)\n{state}";
        }

        // ---- helper: forwards a collision on one object to this controller -------------------------------------

        class CollisionRelay : MonoBehaviour
        {
            GameObject only;
            Action callback;

            public void Init(GameObject onlyFrom, Action onHit)
            {
                only = onlyFrom;
                callback = onHit;
            }

            void OnCollisionEnter(Collision c)
            {
                if (callback == null) return;
                if (only != null && c.gameObject != only && (c.rigidbody == null || c.rigidbody.gameObject != only)) return;
                callback();
            }
        }
    }
}
