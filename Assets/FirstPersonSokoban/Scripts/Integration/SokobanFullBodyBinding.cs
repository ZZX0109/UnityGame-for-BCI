using FirstPersonSokoban.Core;
using FirstPersonSokoban.PlayerSystem;
using Neuralife.VirtualHand;
using Neuralife.VirtualHand.Body;
using UnityEngine;

namespace FirstPersonSokoban.Integration
{
    /// <summary>
    /// Game01 (Sokoban) 与 Game04 VirtualHand 全身模块的运行时绑定。
    ///
    /// 职责:
    ///   1. [RuntimeInitializeOnLoadMethod] 引导:无需改动任何场景即可启动;
    ///   2. 在玩家脚下实例化 FirstPersonFullBody 全身预制体,并用模块的
    ///      GameIntegrationAdapter 驱动(站立姿态 SetSeated(false) +
    ///      第一人称可见 SetFirstPerson(true),只隐藏头部渲染);
    ///   3. 每帧把玩家真实水平速度 (m/s) 与 yaw 角速度 (deg/s)
    ///      FeedLocomotion 进模块,驱动下肢走跑循环;
    ///   4. 按 V 在 Realistic / Blue 外观间切换(不与现有 Escape/R/Z/E 冲突);
    ///   5. 手势推箱:实现 IGameHandActionAdapter,只消费 HandInteractionRouter
    ///      的 GraspStart 离散边(不转发按住状态),转发到 PuzzleGameManager
    ///      的公开入口 TryPush(),绝不直接改箱子位置或规则状态。
    /// </summary>
    public sealed class SokobanFullBodyBinding : MonoBehaviour, IGameHandActionAdapter
    {
        // 模块预制体路径:预制体已移入 Resources 目录,玩家构建可用
        // Resources.Load("FirstPersonFullBody") 解析(见 FullBodyResourcesCandidates);
        // 编辑器下也可按该路径直接 AssetDatabase 加载。
        const string FullBodyPrefabPath = "Assets/FirstPersonSokoban/Resources/FirstPersonFullBody.prefab";
        const string HandsPrefabPath = "Assets/VirtualHand/Prefabs/ElenaFF/FirstPersonHands.prefab";

        // Resources.Load 的候选路径(相对任一 Resources 根目录,不含扩展名)。
        static readonly string[] FullBodyResourcesCandidates =
        {
            "FirstPersonFullBody",
        };
        static readonly string[] HandsResourcesCandidates =
        {
            "FirstPersonHands", "VirtualHand/Prefabs/ElenaFF/FirstPersonHands",
        };

        const float TeleportPlanarSpeed = 10f;   // m/s;超过视为传送,不喂走跑速度
        const KeyCode AppearanceKey = KeyCode.V; // 已占用键:Escape/R/Z/E + 移动/鼠标

        static SokobanFullBodyBinding _instance;

        /// <summary>可选手动挂接:外部引导脚本可在实例化前指定预制体。</summary>
        public GameObject fullBodyPrefabOverride;
        public GameObject handsPrefabOverride;

        GameIntegrationAdapter _bodyAdapter;      // 模块提供的身体桥接(位于全身实例上)
        GameObject _bodyInstance;
        PuzzleGameManager _game;
        FirstPersonController _player;
        Vector3 _lastPlayerPos;
        float _lastYaw;
        FullBodyAppearanceMode _appearance = FullBodyAppearanceMode.Realistic;
        int _lastPushFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject("SokobanFullBodyBinding");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<SokobanFullBodyBinding>();
        }

        void Awake()
        {
            _instance = this;
            SetupGestureStack();
        }

        // ------------------------------------------------------------------
        // 手势栈:FirstPersonHands 预制体(自带 VirtualHandHost/HandInputRouter/
        // 双 HandRigDriver)+ 补挂 HandGestureRecognizer 与 HandInteractionRouter。
        // 路由器把“握拳/捏合按住状态”转成离散 GraspStart/GraspEnd 边,
        // host 是边的唯一消费者 → 本类 adapter.OnHandUpdate 每条边只收一次。
        // ------------------------------------------------------------------
        void SetupGestureStack()
        {
            var prefab = ResolvePrefab(handsPrefabOverride, HandsPrefabPath, HandsResourcesCandidates);
            if (prefab == null)
            {
                Debug.LogError("[SokobanFullBodyBinding] 找不到 FirstPersonHands 预制体。" +
                               "请把 " + HandsPrefabPath + " 放入任意 Resources 目录,或在引导场景里" +
                               "给 SokobanFullBodyBinding.handsPrefabOverride 赋值。手势推箱将不可用。");
            }

            GameObject hands = prefab != null ? Instantiate(prefab) : new GameObject("FirstPersonHands");
            hands.name = "SokobanVirtualHandRoot";
            DontDestroyOnLoad(hands);

            if (hands.GetComponent<HandGestureRecognizer>() == null)
                hands.AddComponent<HandGestureRecognizer>();   // Awake 自动收集子级 HandRigDriver
            if (hands.GetComponent<HandInteractionRouter>() == null)
                hands.AddComponent<HandInteractionRouter>();   // Awake 自动绑定同物体识别器

            var host = hands.GetComponent<VirtualHandHost>();
            if (host == null) host = hands.AddComponent<VirtualHandHost>();
            host.interactionRouter = hands.GetComponent<HandInteractionRouter>(); // host.Awake 可能早于路由器
            host.adapter = this;                                                  // 本类即游戏侧适配器
        }

        // ------------------------------------------------------------------
        // 每帧:定位玩家 → 放置/跟随全身 → 喂运动数据 → 外观切换键。
        // LevelLoader.ClearLevel() 会销毁并重建 Player,所以每帧重解析。
        // ------------------------------------------------------------------
        void Update()
        {
            if (Input.GetKeyDown(AppearanceKey))
            {
                _appearance = _appearance == FullBodyAppearanceMode.Realistic
                    ? FullBodyAppearanceMode.Blue
                    : FullBodyAppearanceMode.Realistic;
                if (_bodyAdapter != null) _bodyAdapter.SetAppearanceMode(_appearance);
            }

            if (_game == null) _game = FindObjectOfType<PuzzleGameManager>();
            var player = _game != null && _game.Loader != null ? _game.Loader.Player : null;
            float dt = Time.deltaTime;

            if (player == null)
            {
                // 玩家不存在(菜单/关卡切换):身体停在原地,运动清零。
                // _lastPlayerPos 保持上次值,防止回到游戏瞬间速度尖峰。
                if (_bodyAdapter != null && dt > 0f) _bodyAdapter.FeedLocomotion(Vector2.zero, 0f);
                return;
            }

            if (_bodyAdapter == null) CreateBody();
            if (_bodyAdapter == null) return;

            Transform playerT = player.transform;
            float yaw = player.LookYaw;
            Vector3 pos = playerT.position;

            // 玩家切换(关卡重建):快照姿态并对齐身体。
            if (_player != player)
            {
                _player = player;
                _lastPlayerPos = pos;
                _lastYaw = yaw;
                _bodyInstance.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
                _bodyAdapter.SetSeated(false);            // 站立为默认姿态
                _bodyAdapter.SetFirstPerson(true);        // 只隐藏头部渲染,身体可见
                _bodyAdapter.SetAppearanceMode(_appearance);
                return;
            }

            if (_bodyInstance != null)
            {
                // 全身根节点 = 预制体脚底原点 → 直接对齐玩家根(地面)与 yaw。
                _bodyInstance.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            }

            if (dt <= 0f) return;

            Vector3 delta = pos - _lastPlayerPos;
            delta.y = 0f;
            _lastPlayerPos = pos;

            Vector3 worldVel = delta / dt;
            float planarSpeed = worldVel.magnitude;
            // 3600 deg/s:高于连续旋转可达速率约 5 倍,同时远低于平滑转向帧间增量的误报阈值
            // (60fps 下单帧 60° 才会触发);1 秒内 8 圈只可能是传送/重生/瞬间朝向重置。
            if (planarSpeed > TeleportPlanarSpeed || Mathf.Abs(Mathf.DeltaAngle(_lastYaw, yaw)) / dt > 3600f)
            {
                // 传送(Teleport/RestartLevel):跳过本帧,不产生速度尖峰。
                _lastYaw = yaw;
                _bodyAdapter.FeedLocomotion(Vector2.zero, 0f);
                return;
            }
            // 世界速度 → 玩家身体局部平面分量 (x=右, y=前),单位 m/s。
            Vector3 fwd = playerT.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = playerT.right; right.y = 0f; right.Normalize();
            var planar = new Vector2(Vector3.Dot(worldVel, right), Vector3.Dot(worldVel, fwd));
            float yawRate = Mathf.DeltaAngle(_lastYaw, yaw) / dt; // deg/s
            _lastYaw = yaw;
            _bodyAdapter.FeedLocomotion(planar, yawRate);
        }

        void CreateBody()
        {
            var prefab = ResolvePrefab(fullBodyPrefabOverride, FullBodyPrefabPath, FullBodyResourcesCandidates);
            if (prefab == null)
            {
                Debug.LogError("[SokobanFullBodyBinding] 找不到 FirstPersonFullBody 预制体。" +
                               "玩家构建需以 'FirstPersonFullBody' 从 Resources 目录加载" +
                               "(应在 " + FullBodyPrefabPath + "),或给 " +
                               "SokobanFullBodyBinding.fullBodyPrefabOverride 赋值。全身显示将不可用。");
                return;
            }

            _bodyInstance = Instantiate(prefab);
            _bodyInstance.name = "SokobanFullBody";
            if (Application.isPlaying) DontDestroyOnLoad(_bodyInstance);
            _bodyInstance.transform.SetPositionAndRotation(
                new Vector3(0f, 0f, 0f), Quaternion.identity);

            // 预制体只含 5 个身体组件;GameIntegrationAdapter.Awake→Wire() 会自动补齐接线。
            _bodyAdapter = _bodyInstance.GetComponent<GameIntegrationAdapter>();
            if (_bodyAdapter == null) _bodyAdapter = _bodyInstance.AddComponent<GameIntegrationAdapter>();
            _bodyAdapter.SetSeated(false);
            _bodyAdapter.SetFirstPerson(true);
            _bodyAdapter.SetAppearanceMode(_appearance);
        }

        // ------------------------------------------------------------------
        // IGameHandActionAdapter:手势 → 游戏动作的唯一入口。
        // 姿态类手势转发给模块 adapter(手臂摆姿);游戏动作只认 GraspStart 边。
        // ------------------------------------------------------------------
        public string GameName => "Sokoban";

        public void OnHandUpdate(HandTrackingFrame leftFrame, HandTrackingFrame rightFrame,
                                 HandGestureEvent leftGesture, HandGestureEvent rightGesture)
        {
            if (_bodyAdapter != null)
                _bodyAdapter.OnHandUpdate(leftFrame, rightFrame, leftGesture, rightGesture);

            bool graspStart = leftGesture.type == HandGestureType.GraspStart
                           || rightGesture.type == HandGestureType.GraspStart;
            if (!graspStart) return;
            if (_lastPushFrame == Time.frameCount) return; // 双手同帧各自一条边 → 只推一次
            _lastPushFrame = Time.frameCount;

            if (_game == null || _game.Phase != GamePhase.Playing || !_game.ControlsEnabled) return;

            // 仅调用公开入口:内部自检瞄准/对齐/推送状态,不触碰箱子与规则数据。
            _game.TryPush();
        }

        // ------------------------------------------------------------------
        // 预制体解析:手动挂接 → Resources → 编辑器 AssetDatabase(播放模式可用)。
        // ------------------------------------------------------------------
        static GameObject ResolvePrefab(GameObject manual, string assetPath, string[] resourcesCandidates)
        {
            if (manual != null) return manual;
            foreach (string candidate in resourcesCandidates)
            {
                var fromResources = Resources.Load<GameObject>(candidate);
                if (fromResources != null) return fromResources;
            }
#if UNITY_EDITOR
            var fromAssets = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (fromAssets != null) return fromAssets;
#endif
            return null;
        }
    }
}
