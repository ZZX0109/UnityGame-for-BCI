# 过街课程、街区难度与建模

## 六阶段训练与街景验收（2026-11-20）

六阶段从同一课程菜单进入，可以单独练习，也可以按顺序继续。九项街区任务、三组车流配置和原有成绩仍各自保存。

| 阶段 | 玩家要完成的动作 | 场景中必须能看清的线索 |
|---|---|---|
| 熟悉环境 | 留在人行道，走到路沿，看向对面的行人灯 | 车行道边缘、盲道警示面、横道入口、对面灯面 |
| 单方向观察 | 在路沿向左看清近侧车道 | 行人视点到来车方向的视线。练习在路沿结束 |
| 双方向判断 | 同时考虑两侧车流和灯相，沿横道去便利店 | 双侧来车方向、行人图标、对面店铺入口 |
| 复杂车流 | 分辨近车与远车，安全穿越商业街四车道 | 车道边界、不同车身轮廓、车辆是否真正停稳 |
| 干扰和遮挡 | 在人行道上换位，绕开站牌对近车道的遮挡 | 不透光的站牌实体、牌边可观察的位置和转弯车辆 |
| 生活情境任务 | 去咖啡店并返回，两个方向各自重新观察 | 可辨认的店门、店前桌椅和住宅入口，返程的信号朝向 |

行人视角优先检查店前区、连续通行带、设施带的关系。首轮将路沿收口、铺砖方向、店前布篷、木檐、住宅阳台与远景窗列分别建模；车辆按车种区分轮拱、侧窗和车尾，信号板保留深色背景。近景树干上方的分叉、叶团和叶尖是本项目原创的三个共享 OBJ（CrownBranches、CrownFoliage、CrownLeafTips）。本轮照明使用较中性的日光，路面颗粒按米制 UV 铺设。布置依据和眼高参考见 [GDCI 人行道分区](https://globaldesigningcities.org/publication/global-street-design-guide/designing-streets-people/designing-for-pedestrians/sidewalks/)、[上海街区设施与立面图例](https://www.shanghai.gov.cn/cmsres/f0/f04f2063ea60454c8783a82f7a0ed728/6dc5fa092c6b322e50695822b6fa0b58.pdf)和 [FHWA 信号背板参考](https://highways.fhwa.dot.gov/safety/proven-safety-countermeasures/backplates-retroreflective-borders)。

上游成品模型分别用于咖啡店门旁和检修井盖外侧。署名、固化版本及改动见 [KayKit 餐桌餐椅](Game03_StreetCrossing/KayKitRestaurant/SOURCE.md) 和 [Khronos 路锥](../Assets/StreamingAssets/TrafficCone-Attribution.txt)。

**下一轮优先级**

本小时源码审查补出一个需先于几何调整核实的交互：`BuildSideJunction` 在 `x=-9` 放置的 `RoadwayHazard` 区域覆盖 `z=±10` 的人行道导向带；`TickWorld` 合并区域标志后，`CrossingEvaluator` 会把该标志命中按主横道逻辑开始过街。这可能把沿人行道走动误记为过街或违规，目前没有运行时日志证明，不能先改评估器或把区域命中算成真实危险。新增验收逐区记录名称/实例 ID、位置、信号、`withinRoute`、评估事件和近失事件；先核实两侧沿线通过 `x=-9` 的行为。若确实触发评估事件，待独立课程路线定义后再决定修复。源推演的最近余量约 9 mm，因此原 1 cm 采样阈值不足以分辨边界，局部加密目标改为净距变化 ≤1 mm 且交叠分类稳定，否则保持“近界/待定”。详细任务已更新在 [GLM 工作包](../BuildOutput/RouteTactile-20261120224722/glm-task-20261121.md)。

2026-11-21 本小时，经 [Chrome 网页 ChatGPT 评审](https://chatgpt.com/c/6b000f79-6bd4-83ec-8239-891f3ba95151)，当前首项是先生成 S03 `x=-9` 路口运行时俯视叠图，再决定是否修改短支线。图中要同时标出两侧人行道边界、经独立路线设计确认的行人穿越范围、等待点、盲道末段朝向和左右转车辆扫掠区；警示带范围本身不能直接当作过街范围。视点固定为正交俯视（中心 `(-9,0)`、`+z` 朝上、米制比例尺），并补齐南北两侧 1.77 m 眼高、76° FOV、1920×1080 的来向/去向图及最接近支线的车辆姿态。左、右转各核 Sedan、Hatchback、Van 共六条轨迹；记录支线越出已确认行人范围的最大距离、等待点胶囊至车身扫掠区的最小有符号距离。原评审先建议 1 cm；考虑本轮源推演约 9 mm 的近界余量，当前验收改为步长减半后局部加密至净距变化 ≤1 mm 且分类稳定，否则标为近界/待定。1 mm 和 1 cm 均是项目判据，不是法规阈值。穿越范围尚无独立依据时，不删线或宣布通过，状态写“支线用途待定”。

本轮三路独立审阅认为南侧主导向带在两种视角中连续，另有一条约 90° 短支线接到警示带；可能产生方向歧义，但图像不能证明玩家会跟随或发生碰撞。几何审阅按车辆路径源码重建 OBB，发现南右转和北左转车身平面投影与横向导向段相交；对新增窄连接件的估算最小间距约 0.153 m，计入 0.12 m 路肩余量后约 0.009 m。以上是源码数学推导，不是 Unity 运行时测量。已有车辆净空用例只检查物理碰撞体，而盲道组件是无碰撞体的视觉件，所以该 PASS 不能回答动态车辆与导向面相交问题。下一步须在运行时记录真实 OBB 姿态与盲道三角面关系，并补北侧路线连通性，之后才决定保留或局部修正支线。详细问题、阈值、来源和限制见 [2026-11-21 路线评审](../BuildOutput/RouteTactile-20261120224722/review-20261121.md)。

后续两包依次为六阶段事件记录与一处生活场景细化、桌面与PICO各自验收。几何可见、玩家朝向和实际识别分别记录，不能互相代替。网页来源清单、三包详细验收与限制已归档于 [网页回复证据](../BuildOutput/Realism-BusStop-20261120/chrome-planning-response-ui.txt)。已打开核对 [GB55011发布公告](https://www.mohurd.gov.cn/gongkai/zc/wjk/art/2021/art_17339_761190.html)、[Kim、Torrens 2024原始论文](https://www.rkim.dev/publications/Kim2024Building/3615979.3656060.pdf)及 GB 55019-2021 官方全文：§2.11.3 规定提示盲道位置、范围对应长度及至少 300 mm 宽度，§2.11.4 要求与邻接铺面在颜色或材质上有差异。后者是几何识别要求，不代表虚拟路口通过合规审查；具体适用见 [S03 路线评审](../BuildOutput/RouteTactile-20261120224722/review-20261121.md)。Kim、Torrens 论文是 ACM SIGSIM PADS 小样本演示，支持关注交通互动真实感，不能证明 Game03 训练有效或否定美术细化。网页提及的铺装、树木和椅子资源仅为候选，未导入或验收。

1. 已在课程第三至第六阶段复现并修复旧观察仍能得安全分的问题。任一侧超过 10 秒、信号切换或该侧来车的接近／停稳／进入冲突区等状态改变，会提示在路沿重新观察对应方向。等待期间不扣星；重看后照常可以安全通过。下一步在头显上检查提示是否抢占行人信号的注意力，并观察高流量下重新观察的频率。
2. 在同一入口和目的地采集 PICO 双眼视图，核对抬头找灯、转头找远车、遮挡后换位，记录帧时间与阅读距离。逐步给密集街景做远近模型切换。
3. 丰富直接影响判断的交通参与者，如路边乘客与来车的遮挡组合。先做可复现的时序和站位，评估是否引入额外危险，再加动画和模型。

### 行人视角下的街景建模跟踪

2026-11-20 夜间，S03 对街 `Detailed bus shelter` 的五条座板由顶高约 0.6775 米降至 0.45 米，并补齐可见座腿、鞋脚、托架与横向连接件；旧通用座面渲染关闭，其既有碰撞体同步到新座面范围。支撑是纯渲染部件，Collider 总数仍为 91。固定 1920×1080、眼高 1.77 米、FOV 76° 的同相机 PlayMode 捕获：改前 1/1、改后两次各 1/1；几何回归 4/4、S03 交互验收 2/2。清单断言座面高度、支撑边界、路牌净空和碰撞体数量；尚未进行 PICO 头显验证。0.45 米是项目尺度选择，[乌鲁木齐市政府公开的座椅实例](https://www.wlmq.gov.cn/wlmqs/c119052/202606/f37f16dc71954cf5b75347a8762fa37e.shtml)仅作尺寸参考，不构成上海规范。[改前图与 manifest](../BuildOutput/Realism-BusSeat-20261120T2042/before/) · [改后两次图与 manifest](../BuildOutput/Realism-BusSeat-20261120T2042/after/) · [测试 XML、Unity 日志与分工](../BuildOutput/Realism-BusSeat-20261120T2042/)

2026-11-20 首轮 S03 `x=-10` 修复将基线 10.30 m 的视觉导向缺口连接起来，采用三个按铺装高程分段的行进带和一个中心短支线，而非补 10.3 m 单条直线。南侧两种固定视角、两次独立 accept 运行及 240 点单线静态胶囊扫描留有归档结果；但只有南侧栅格连通、未实际驱动 CharacterController，也未检查北侧连通或车辆与视觉几何的动态交互。因此它只支持“南侧画面中的导向带连续”和“该单线没有新增静态阻挡”，不支持双侧可走性、触觉可用性或交通安全。归档 XML 每次各 1/1 个目标用例通过，几何回归为 4 通过/2 跳过。旧 README 的“8/8”和“8 跳过”计数已纠正；旧 `sourceSha256` 是非哈希环境标签，当前工作树与归档字节一致性未证明。下一轮按上方俯视叠图计划补齐证据；不得把 10.30 m 作为必须铺设长度。[运行截图、manifest 与测试](../BuildOutput/RouteTactile-20261120224722/) · [本轮独立评审](../BuildOutput/RouteTactile-20261120224722/review-20261121.md)

2026-11-20 傍晚，S03 路口遮挡站亭已补薄顶片、檐框、两根落地立柱、牌框支腿、分条木座板与腿架。站牌按站名、线路、方向三层排布。旧不透明牌及四个碰撞体保留，新增左前柱具有匹配碰撞体；同机位复拍使用相同眼位、76° FOV和冻结车流。站亭渲染器由14增至15，三角面由144增至3476；PICO帧时间尚未实测。座面顶仍为0.525米、顶棚最高2.39米，本轮保留既有高度，尺寸细化待后续权衡。ZCode GLM负责后续支撑修复与回归/构建，三名Luna max独立复核；原生玩法回归14/14通过（0失败、0跳过），Windows构建成功；本轮未重打PICO。[修改前](../BuildOutput/Realism-BusStop-20261120/before/before-station-detail.png) · [修改后](../BuildOutput/Realism-BusStop-20261120/after/after-station-detail.png) · [工作分配](../BuildOutput/Realism-BusStop-20261120/work-allocation.json)

2026-11-20 下午，Poly Haven Asphalt 02 的 2K 颜色、法线和粗糙度已接入共享沥青材质。主路、支路、转弯路肩与远景使用同一街区坐标，每 3 米重复一次。粗糙度反相写入光滑度通道，Android 采用 ASTC 6×6 和 mipmaps。原文件哈希与 CC0 出处随包保存。[素材来源及处理方式](../Assets/StreamingAssets/PolyHaven-Asphalt02.txt) · [本轮验证目录](../BuildOutput/Realism-Asphalt-20261120/)

2026-11-20 午后，三家任务店面已从同一套开间分化。书店用三扇窄高窗和两侧书展窗，药房用连续横窗、浅平檐和 1.10 米宽的入口玻璃，咖啡店用两扇分格上窗并保留斜篷与陈列柜。三扇任务门的位置逐一核对一致。两机位各采集改前、改后和遮住招牌的画面；店前有树，近景另选了越过树干的位置。书店和药房的货架商品仍然相似，可用书脊与封面、药盒和药瓶的不同轮廓来区分。[最新版近景](../BuildOutput/Realism-Facades-20261120/current-shop-row.png) · [同机位改前](../BuildOutput/Realism-Facades-20261120/before/before-opposite-curb.png) · [同机位改后](../BuildOutput/Realism-Facades-20261120/after/after-opposite-curb.png) · [几何测量](../BuildOutput/Realism-Facades-20261120/after-geometry.csv)

2026-11-20，S03 咖啡店门前已用两个固定机位复拍。近看能辨认杯口、杯柄、杯碟和后层货架，店外餐椅座面从 35 厘米提高到 45 厘米，两只调味瓶从约 55 厘米缩到 16 厘米。两侧建筑之间已补上后院地面，桌后缝隙原来露出的天色已消失。截取行人眼高画面时冻结同一场景，门垫、桌椅与人行道一起入镜。[修改前近景](../BuildOutput/Realism-Shop-20261120/before-shop-display.png) · [修改后近景](../BuildOutput/Realism-Shop-20261120/after-shop-display.png) · [修改后步行视角](../BuildOutput/Realism-Shop-20261120/after-shop-approach.png)

| 优先项 | 在哪里能看出问题 | 改动怎样算有效 |
|---|---|---|
| 待返修 · 高 · S03 转弯路肩—主路接缝（2026-11-21） | 原平铺路肩与主路顶面相差 31 毫米。候选高度场沿主缝方向渐降，并非横跨接缝的行进坡；改后真实主缝样点在距角点 0.08 米处仍差 27.9 毫米、0.58 米处差 11.9 毫米。更远探针命中 `Paved sidewalk`，所以 `stepOutsideTransition=0` 不能证明剩余主缝齐平。候选 EditMode XML 缺失；侧缝探针方向及角点指标也有待修正。 | 先判定同一角点处主路顶 −0.010 米、侧街顶 +0.0205 米是否有意保持 30.5 毫米高差；单个单值 apron mesh 无法同时吻合。再重新定义主路、侧街、路肩、人行道的边界归属，并沿实际共享边逐点核实相邻 Collider。若主缝设计为平接，项目工程门限为每个有效样点 ≤1 毫米（此数值不是法规限值）；若保留不同标高，须让裁切后的 junction/corner mesh 明确负责连接，不能靠沿边渐变。保留相同眼高/FOV 前后图，再补低角度斜光图；PlayMode 当前 1/1 只证明捕获用例运行通过，不代表几何已验收。PROWAG R304.2.3/R304.5.4 将 curb-ramp/blended-transition 的相接坡面要求为平齐且不允许高差，仅作形态比较参考，不作为本项目适用法规或数值门限：[Access Board 技术条文](https://www.access-board.gov/prowag/technical.html)。证据：`BuildOutput/StreetApronTransition-20261121T0434/`。头显实测未做。 | 独立复核补充：侧街 Box 约 84.6 m 长；整体下移 30.5 mm 将与主路 slab 在交叉口重叠约 25 mm，并改变车辆与人行道的标高关系。该方案仍属候选推断；道路并集、apron 与 Paved sidewalk 必须分别判定 owner 和采样，不得只调 Box 高度后记为通过。
| 待确认 · 中 · S03 支路口路肩/侧街/人行道三面交界 | 改后偏移探针记录最大 20.5 毫米，但部分射线命中人行道铺装；侧缝偏移方向有误，故暂不能把该数值归属到已确认的实际行走边。 | 修正探针的内外方向和面分类；从南北两侧各采样实际相邻表面，记录世界坐标、对象名和高差，再决定是否共面、加缘石或做过渡。若确认有意外台阶，再按同机位和步行路线复验。 |
| 中 · 路口站亭尺寸与远景负载 | 本轮已补完整支撑、薄棚与分条座板；座高0.525米、棚高2.39米仍沿用既有空间，远景无细节分级。 | 结合实际地坪复核座高和棚下净高；在头显测同机位帧时间后决定远景简化。调整后仍须验证遮挡和在人行道内安全换位。 |
| 已完成 · 对街站亭座椅 | S03 `Detailed bus shelter` 座面约 0.6775 米，旧通用座面顶 0.63 米且没有可见支撑。 | 已改为顶高 0.45 米并补可见支撑，隐藏重复渲染、对齐既有 Collider。固定机位前后和改后复测均通过；总 Collider 数保持 91。后续若改连接件，需先验证视觉接合与当前无新增碰撞的性质。证据见 `BuildOutput/Realism-BusSeat-20261120T2042/`。 |
| 进行中 · S03 x=-9 转角导向支线 | 南侧两视角显示主导向带连续，但 90° 支线接入警示带；源公式推演显示车辆扫掠可能靠近或覆盖铺装。另有 `RoadwayHazard` 盒体可能与 `z=±10` 侧人行道相交，导致路沿/区域分类改变；这是源码推演风险，尚未由步行事件确认。无北侧连通图、实际行走或运行时车身叠图。 | 先补运行时正交俯视叠图、北侧路线洪泛、六条转弯轨迹及等待点有符号净距；用实际步长减半复算。实际步行时同步记录 hazard 区域、`IsAtKerb` 与 evaluator 事件，确认是否触发错误分类。穿越区域需由盲道以外的场景路线证据确认；证据不足时保持“用途待定”。不得以几何公式推演代替 Unity 运行测量。 |
| 待复验 · 中 · 六阶段当前分支行为回归 | 当前源码有 9 个 MissionAcceptance 任务配置的通关测试包装器，CourseObservationRefresh 覆盖 S01-T1 的真实步行、车流变化与重新观察；但最近可查 XML 是 11 月 20 日的旧分支运行记录，不能作为当前脏工作树 PASS。 | apron 返修收敛后，在当前候选源码上重跑任务配置回归与观察时效用例；重点记录 S02-T2、S03-T2 遮挡换位和 S03-T3 往返是否逐段实际完成，返程是否需要重新观察。保留日志、XML 与源码指纹；不把旧包的 9/9 或 147/147 继承到当前候选。 | 覆盖审计补充：阶段 4–6 当前仅有映射/部分配置检查，没有分别通过 StartCourse(3/4/5) 实际走完课程；阶段 1–3 的现有行为 XML 也来自 11 月 20 日旧分支。待 GLM 几何任务释放后，先对固定源码指纹复跑现有阶段 1–3 测试，再独立补阶段 4–6 行为轨迹，保存 XML、事件日志、源码指纹和前后截图。
| 较高 · S03 路口 approach 右侧大片空白侧墙 | 眼高 1.77 m 的 approach 画面中，灰墙占画面右侧较大区域，只有一个孤立暗窗；它比接缝前后变化更显眼，与对街店面细节反差强。 | 先从固定路口视点增加真实窗洞进深、窗框/玻璃层次、墙面材质变化和首层基座，再用正视与斜视两机位对照；确认没有遮住行人灯、横道入口或转弯车辆。对应证据：`BuildOutput/StreetApronTransition-20261121T0434/after/after-approach.png`。资源许可与适配记录：[GitHub/写实数据筛选](../BuildOutput/Realism-AssetReview-20261121-0703.md)。 | 下一项仅在几何返修释放后执行；候选同机位为 1920×1080、眼高 1.77 m、FOV 76°，位置 (−17.5,1.77,−10)、目标 (−9,0.02,−10)。建议门（项目验收提案，非标准）：至少 2 个窗格、玻璃退入 ≥0.08 m、窗台外挑 0.08–0.15 m、连续基座高 0.30–0.45 m、每格在 approach 图中 ≥20 px；不新增 Collider，且不遮挡信号、横道入口或转弯车辆。
| 中 · 陈列后方的室内空间 | 从书店和药房的入口看，货架后的整片深色仍给人实心面板的感觉；咖啡杯后方也缺少室内纵深。 | 先选一家，在步行可达的两个斜角比较窗框、货架和后墙的相对移动与明暗。从对街回看时，展示窗不能盖住门、行人灯或牌面。 |
| 中 · 行道树近景 | 在药房前的路沿向上看，树冠底部像一片连续的深色板面；从这处拍正面，树干恰好挡住店门。原位与越过树后的两张图可交叉检查。 | 比较树下抬头、正常平视和移步后三个视角，检查枝叶层次与透光。保留行道树形成的自然遮挡，并复核横道入口仍能观察行人灯。 |

公交站的站名、线路信息、座椅和侧面挡风板可参照上海候车亭图例。开源模型 `ponto_onibus` 可参考顶棚与立柱的连接形式。该模型属于巴西城市套件，本轮仅作造型参考。[上海候车亭与站台构件图](https://jtw.sh.gov.cn/cmsres/e3/e370e685f26742a8b69c5be0a568be09/7ed0ad2e47d3539fa86a2d90352f047d.pdf) · [作者仓库与 CC0 许可](https://github.com/kayquer/kit-cidade-lowpoly)

店面尺度与橱窗纵深的后续参考包括上海企业天地店的沿街玻璃面及店内陈列、开源街角商铺的开口和体量组合。本轮模型由项目内几何生成，参考资产未导入。[企业天地店实景](https://www.starbucks.com.cn/design-studio/stores/corporateave/) · [固定版本的街角商铺模型](https://github.com/kayquer/kit-cidade-lowpoly/blob/d9de7e25d476d8b8572971840b129c28a68e9cf7/models/comercial/loja_esquina.glb) · [对应版本 CC0 许可](https://github.com/kayquer/kit-cidade-lowpoly/blob/d9de7e25d476d8b8572971840b129c28a68e9cf7/LICENSE)

#### 写实资产候选（2026-11-20，尚未导入）

| 目标 | 候选、文件来源与许可 | 在 Game03 的用法 |
|---|---|---|
| 商铺外墙、入口与地坪交接 | Drone Building Scans 的 flat-roof 实体商业楼。该栋用实物标定米制尺度，含砖墙、彩板、落地玻璃与人行道。GitHub 有校验和与拍摄说明，22 MB 的贴图模型由作者放在 Hugging Face，CC BY 4.0。[固定版项目](https://github.com/Matt1Up/drone-building-scans-dataset/tree/19b3ce9cccf3e8fd414ff705f75d0f2105846fc1) · [原始 GLB](https://huggingface.co/datasets/Matt1up/drone-building-scans/resolve/main/flat-roof/model/flat-roof.glb) | 优先用这栋的玻璃、踢脚与砖墙过渡校准一间店，保留游戏街区的中文店招及既有通行布局。 |
| 店面与咖啡店内外的层次 | Amazon Lumberyard Bistro。GitHub 有 PBRT 场景，ORCA 有 FBX 原版。授权 CC BY 4.0，使用时保留 Amazon Lumberyard 署名、许可与改动说明。[固定版 GitHub 场景](https://github.com/mmp/pbrt-v4-scenes/tree/30cf4a0346ae5a80a2d7a530a3ef7d0fa4f70572/bistro) · [ORCA 原作与许可](https://developer.nvidia.com/orca/amazon-lumberyard-bistro) | 看窗洞、门框、遮雨篷的厚度和室内明暗；只选适合本街区的构件。原外景有 2,832,120 个三角面。 |
| 便利店橱窗后的商品与照明 | Khronos Commercial Refrigerator，仓库内可下载 GLB。Darmstadt Graphics Group / Eric Chadwick、原作者 Sean Thomas，CC BY 4.0。[固定版模型、制作说明及署名](https://github.com/KhronosGroup/glTF-Sample-Assets/blob/f0920f5b32d172a30efe3f9257e965918831e6ea/Models/CommercialRefrigerator/README.md) | 把柜体放在玻璃后方，保留货架、内照明和门封条的层次。PICO 首版改用简化玻璃；原件需要透射材质。 |
| 咖啡店陈列 | Khronos Diffuse Transmission Teacup，GLB 中有薄瓷杯与碟，模型和纹理 CC0。[固定版模型](https://github.com/KhronosGroup/glTF-Sample-Assets/blob/f0920f5b32d172a30efe3f9257e965918831e6ea/Models/DiffuseTransmissionTeacup/README.md) · [逐资产许可](https://github.com/KhronosGroup/glTF-Sample-Assets/blob/f0920f5b32d172a30efe3f9257e965918831e6ea/Models/DiffuseTransmissionTeacup/LICENSE.md) | 原件约 28,184 个三角面，超过当前三家任务店面的网格合计。保留杯沿弧线与把手剪影后减面，再摆放多个实例；PICO 首版用普通陶瓷材质。 |
| 民用公交站的骨架参考 | Ponto Ônibus，可直接获取的 CC0 GLB，480 个三角面，采用巴西城市套件的配色和造型。[模型文件](https://github.com/kayquer/kit-cidade-lowpoly/blob/d9de7e25d476d8b8572971840b129c28a68e9cf7/models/mobiliario/ponto_onibus.glb) · [许可](https://github.com/kayquer/kit-cidade-lowpoly/blob/d9de7e25d476d8b8572971840b129c28a68e9cf7/LICENSE) | 可参考顶棚、座椅和立柱关系。近景仍按上海站亭的板材厚度、固定件和线路信息重新建模。 |
| 沥青与墙面 | Poly Haven 的材质由官方 CDN 提供，GitHub 仓库存放 API 源码。Asphalt 02 与 Brick Wall 001 均为 CC0。[沥青，实宽 3 米](https://polyhaven.com/a/asphalt_02) · [墙砖](https://polyhaven.com/a/brick_wall_001) · [许可](https://polyhaven.com/license) · [GitHub API](https://github.com/Poly-Haven/Public-API) | 先试斑马线旁的一段路面和一家店的侧墙。按米制 UV 对齐尺度，用颜色、法线和粗糙度观察侧光下的变化，并实测头显帧时间。 |

## 九项街区任务的原设计

本轮日期：2026-11-17；保留2026-11-16设计动机。

用户已授权按本稿实施三个街区的九个任务。本文件现为本轮实施目标与验收依据，代码正在集成，新的Unity原生结果尚待验证；2026-11-16四车道版本的PASS不得继承。实施与证据状态见 [CurrentStatus](../Validation/CurrentStatus.md)。本轮保持四车道及保护式信号语义，未来安全岛、无信号横道、行人绿灯与新转弯车辆并行、雨夜等不在本轮范围。

## 设计目标

让玩家完成有明确目的的城市出行，并逐步学会处理不同的过街判断。保留住宅、商业街、转角路口三类街区，先为每类街区设计三个任务梯度，共九个任务配置，复用现有场景。

多样性由目的地、行进方向、任务步骤与道路条件共同产生。难度由需要处理的信息与判断步骤产生。车辆数量、道路宽度和画面装饰不能单独代表难度。

## 实施前源码的边界（2026-11-16基线）

以下是九任务实施前的状态，用于解释变更动机，不代表本轮完成后的功能或验证结论。

- S01、S02、S03 的起点均为 `(0, 0.02, -11.3)`，目的地区域均为 `(0, 0, 10.8)`，名称均为“街角便利店”。目前主要变化来自街景、车流参数与 S03 的遮挡和转弯路线。
- 当前道路是双向四车道，路面总宽 13.2 米。第一轮任务扩展继续使用这一结构。
- 六阶段信号仅在 VehicleGreen 允许车辆入场，仅在 Walk 允许行人开始过街；车辆冲突区未清空时延后放行行人。当前没有行人绿灯与新入场转弯车辆同时放行的训练。
- 左右观察均为必需条件。观察证据来自相机朝向、可见性和停留时间，不是眼动或注意力测量。
- 当前观察超过 10 秒会提示重新确认，但不撤销原有通过记录。是否把“再次确认”纳入新任务评分，需要另设明确规则与回归证据。
- 当前安全成功已要求完成左右观察，星级又为左右观察加一星，因此当前成功结果至少两星，一星不可达；不能直接把旧星级用于九个新任务的难度比较。

## 本轮九个任务安排

| 街区 | 入门任务 | 进阶任务 | 独立任务 |
| --- | --- | --- | --- |
| S01 住宅社区 | 从家门口到对面便利店；逐步提示路沿等待、看灯、左右观察与到达 | 改变到达路沿时的信号阶段，保持车流简单；玩家自主决定等待与出发 | 从便利店返回住宅入口；减少步骤提示，独立完成反向过街 |
| S02 商业街 | 去对面书店；解释四车道都需确认，不能只看最近的一辆车 | 混合车型与排队改变可见范围；辨认受遮挡车道，在人行道安全位置重新观察 | 根据店铺标识找到药店，过街后沿人行道到店；目标位置和步行路线实际改变 |
| S03 转角路口 | 去路口对面的咖啡店；认识转弯路线与信号切换清空过程 | 保留明确可达的安全观察位置；玩家先调整站位，再完成可见道路观察 | 去店铺后返回起点；两段分别判断信号、站位和道路情况，最后汇总 |

任务目标必须对应不同的触发区或实际路线。只改“书店”“药店”的文字，不算新的任务。目的地指引应沿合法过街路线和人行道给出，避免一条直线箭头把玩家引向横道外的车道。第一阶段不加入交易系统、互动交通角色或赶车倒计时。

街景应参与任务辨认：店招对应真实目标，店门连接可行走的人行道，公交站作为明确地标。后续遮挡造型可采用有生活用途的站亭或设施，同时保留经过验证的遮挡范围与安全观察位置。背景角色继续独立活动，不能把跟随他们当成正确通行条件。

## 难度参数与固定保障

| 维度 | 可递进内容 | 固定保障 |
| --- | --- | --- |
| 交通信息 | 简单直行、混合车型、排队、既有转弯路线 | 信号语义稳定；每轮必须存在可完成的安全窗口 |
| 观察条件 | 无遮挡、局部遮挡、需要改变观察站位 | 在人行道内有可达观察点；不能要求先踏入车道才能看清 |
| 决策步骤 | 单次过街、找目标后过街、去程加返程 | 每段单独记录观察与进入条件；不能沿用去程观察直接判返程合格 |
| 教学帮助 | 步骤提示、风险提示与结果解释 | 字号、对比度、控制灵敏度属于可用性设置，不作为加难手段 |
| 配置变化 | 起始相位、出生位置、车辆组合与已验证随机种子 | 保存配置版本与种子，失败可以准确复现 |

每轮引入一个主要新挑战，其余条件尽量固定。不要同时增加车速、车流、遮挡并撤掉全部提示。变化起始信号时，应通过确定性预演或一致的完整状态产生车辆与信号配置，不能仅切换灯色而保留不匹配的车辆位置。

绿灯和清空时间根据路面距离、所选步行速度与起步余量检查。以当前 13.2 米路宽、2 米每秒为例，仅路面步行就需 6.6 秒，还需留出合理余量。不能用迫使玩家跑步或抢灯来制造高难度；调整移动速度后应重新校验安全窗口。

## 反馈、评分与递进

- 以安全抵达为核心，分别记录信号进入是否合法、是否保持合法路线、进入前观察是否有效、是否发生移动碰撞、近失与静止接触。不同事件分开解释，不能都叫碰撞。
- 合理等待与重新观察属于正确行为。耗时用于分析，不用速度奖励抵消违规，也不按等待秒数发分，避免原地刷分。
- 对长时间停留先给可继续的提醒；区分安全等待、暂停和无操作，避免安全等待最终变成强制失败。本轮将现有240/300秒阈值作为非失败提醒，允许继续安全等待；其行为需要新增原生回归证据。
- 提示使用情况单独记录。字号、音量和控制设置不影响安全成绩。
- 失败后给出一个可操作的原因，例如“左侧外车道仍被遮挡，请先在人行道调整站位”，而不是只显示失败。
- 返程等多段任务逐段评价：任何一段存在关键违规，整次任务不能显示安全完成；已完成步骤可保留用于练习复盘。
- 初始升级规则可采用同一技能在三个不同已验证配置中安全完成后推荐下一梯度。此阈值是待试玩校准的游戏设计值，不是测评标准。
- 练习可从验证过的配置池抽取；阶段检查固定配置集，保存任务版本、种子和辅助状态。不能用未经筛选的完全随机配置出题。

## 本轮实施顺序

1. 分开街区 ID、任务 ID 与梯度。保留现有 S01/S02/S03 街区标识，新增任务记录，成绩按任务和配置版本保存，避免相互覆盖。
2. 完成三个单次过街基础任务，并让店铺目标实际生效。新增任务目标与过街完成判定分开，过街后可继续沿人行道到目标。
3. 增加有边界的起始相位和已验证种子变化，以及渐进提示；保持当前信号与让行机制。
4. 增加反向和返程任务。每段重新建立观察与评价状态，不复用上一段的通过标记；检查反向的左右来车与提示语义。交通和信号在两段之间自然继续，不因返程重置为相同开局。
5. 完成九任务的真实行走、失败解释和多配置可解性验收后，再讨论新的交通规则。

实施前配置校验只接受三个情境ID，管理器拒绝重复情境ID。本轮保留三份街区资产，使用独立StreetMissionDefinition/StreetMissionCatalog定义九任务与三组固定seed，不能把九份重复S01/S02/S03配置塞入旧列表。任务成果按任务ID、revision和配置分别保存，旧情境星级不迁移为新任务完成。

## 本轮配置与成绩约定

- 稳定ID为S01-T1至S03-T3，每任务revision=1起步，A/B/C为0/1/2三组固定配置。S01-T3反向；S03-T3去Cafe后返Residence/原住宅侧起点。反向与往返标记不叠加。
- T1为Guided、T2为RiskOnly、T3为OnDemand；T3 独立判断，移除主动帮助入口；等待、字号、音量与舒适设置不降低安全成绩。
- T2预演时长S01=20秒、S02=20秒、S03=8秒；须确定性推进完整信号、生成、车辆运动和清空状态。其他任务不预演。
- 整项安全完成按各段最弱结果汇总。安全完成基础1星，无危险接近加1星，无静止接触加1星；关键违规或移动碰撞的任务0星，不能靠时间或别段成功抵消。
- StreetMissionProgress按任务ID/revision/variant记录最高星；同一梯度三组不同配置安全完成才推荐下一梯度，推荐不锁任务。新模块不读写旧StreetPreferences情境成绩。

## 后续扩展（不在本轮）与验收

四车道版本稳定后，可讨论带安全岛的分段过街。无信号横道、行人绿灯与转弯车并行、雨夜等分别属于新交通规则或新可见性条件，应逐项设计，不在一次升级中叠加。

每个任务配置发布前至少核验：普通步行可完成、存在安全窗口、安全观察点可达、各必需车道确实可见、反向与多段记录不串用、目标与标识一致、提示原因与真实状态一致。所有种子应可复现，并进行真实控制器行走与车辆交互回归；静态配置检查不能替代实际验收。

安全原则参考：

- [NHTSA 步行安全建议](https://www.nhtsa.gov/road-safety/pedestrian-safety)：遵守信号、观察车辆实际行为，并在过街中保持观察。本文的九任务梯度与升级阈值是本项目设计建议。
- [FHWA 行人安全岛说明](https://highways.dot.gov/safety/proven-safety-countermeasures/medians-and-pedestrian-refuge-islands-urban-and-suburban-areas)：安全岛允许行人分次处理一个方向的交通，可作为后续分段任务的基础。

### 2026-11-21 12:15 · apron 候选量测复核

本时段未见共享工作树新增源文件或 Unity 运行结果；不把更早的隔离副本结果写成新进度。当前候选数据说明“捕获测试通过”与“接缝通过”是两件事：

| 项 | 改前 | 隔离候选 | 判读 |
| --- | --- | --- | --- |
| apron 网格 | 4 顶点 / 2 三角 | 8 顶点 / 4 三角 | 候选几何已分段，但未完成改后 EditMode |
| 最大坡度 | 0 | 0.032 | 与项目门限相等；需要从实际最终网格独立复算每三角面 |
| 主缝高差 | 全缝约 31 mm | 距角点 0.08 / 0.18 / 0.28 m 仍约 27.9 / 24.7 / 21.5 mm | 高差沿接缝仍变化，不能按平接验收 |
| 远端探针 | 命中路面与 apron | 命中 Paved sidewalk | owner 错位；零“过渡外台阶”字段不能作为证明 |
| 视角图 | — | approach 与 close-up 同机位无明显可见变化 | manifest 记录 macro 相机高度 0.8 m；低于 1.77 m 玩家眼高，不代表玩家视点 |

改后捕获 XML 1/1、日志 ExitCode 0 只证明捕获用例执行成功，候选目录没有对应的 after EditMode XML/log。ZCode 界面虽显示 GLM 任务仍工作和 1 个 Bash，且模型下拉标签为 glm-5.3-flash/最高，但本机没有相应 Unity Editor 进程、该候选源码 06:15 后未更新；当前周期无可核算的 GLM 有效分钟或工作占比。隔离候选不得回拷或标 PASS。

下一次只在任务完成后继续：先用实际运行时网格和 Collider 对 road、apron、sidewalk、kerb 分别查询，输出共享边/被覆盖区/外露边界与采样 owner；再沿每条边的法向剖面检查高程，不以沿边顶点变化代替跨缝过渡。最终结果需绑定参与运行的源码 SHA，具有改后 EditMode 与 PlayMode XML、完整日志、退出码、几何逐三角坡度和固定眼高图。若任何采样继续命中错误 owner、门限不满足或证据缺失，保持待修，不扩展到车辆或整条侧街。

本轮已并行完成三个独立只读审阅，分别覆盖视觉对比、几何 owner/阈值、实际课程走课缺口；代理 runtime 未公开实际模型 ID/推理档，故“Luna Max 三路”无法核验。Chrome 网页规划会话此前建议先做运行时俯视叠图再决定盲道短支线去留；本轮浏览器读取模型控件连续超时，未发送增量材料、未获得新答复，不重复记作本小时评审。

### 2026-11-21 10:18 · 接缝 owner 与本轮阻断

最新只读复核没有发现 09:27 后的新模型源码、Unity 截图、运行日志或测试 XML。ZCode 的当前 UI 仍显示 apron 返修任务工作中并选择 GLM 标签，但隔离副本和 BuildOutput 未出现本轮结果；这不足以认定改后测试已运行或 GLM 实际占比。保留当前候选，不从临时副本复制回工程。

道路交界不能用单次全场景 Raycast 判定接缝归属：主路和侧街是 BoxCollider，apron 是 MeshCollider，Paved sidewalk 是仍覆盖开口的连续 MeshCollider，另有缘石 BoxCollider。先在隔离副本对主缝、侧缝、角点、开口及路面重叠区运行 RaycastAll，并对每个 collider 单独做命中/三角面查询；清单记录 owner、对象路径、世界坐标/高度、实际三角面和源文件 SHA。必须把 sidewalk 与 road/apron 样点分开；命中多面或 owner 不清时记待定。只有这一层通过后，才评价 apron 完整外露边界、折点约束、每三角面的真实坡度、XZ 覆盖/拓扑及 MeshFilter/MeshCollider 的最终网格，再采集 seam PlayMode 证据。所有毫米及坡度值均为项目验收门，不是法规值。

几何后续先补课程第 4–6 阶段的真实 StartCourse(3/4/5) 玩家输入走课与双段返程证据；当前源码检查主要覆盖映射和部分配置，不能把旧 XML 当作 dirty 源码 PASS。待几何和课程证据稳定后，再单独细化 approach 灰色端墙并用相同 1920×1080、1.77 m、76°机位对比。Chrome 网页 ChatGPT 上一轮建议的几何→走课→端墙顺序仍可用；本小时因原生文件选择器阻塞，未完成新同步。

资料边界：[Unity Mesh.GetTriangles](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Mesh.GetTriangles.html) 可读取索引以构建三角面分析；[MeshFilter.sharedMesh](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/MeshFilter-sharedMesh.html) 与 [MeshCollider.sharedMesh](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/MeshCollider-sharedMesh.html) 说明渲染/碰撞网格的引用关系；[Collider.Raycast](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Collider.Raycast.html) 可针对目标碰撞体查询，但网格引用一致不能替代实际物理命中验收；[libigl Gradient](https://libigl.github.io/tutorial/#gradient) 与 [CGAL 2D constrained triangulation manual](https://doc.cgal.org/latest/Triangulation_2/index.html#secTriangulation_2ConstrainedTriangulations) 仅支持计算和建模方法。[Schneider et al. 2022](https://doi.org/10.1177/0018720820987446) 表明不同 VR/实体环境下的过街决策可能不同，不能由视觉还原推导训练效度。

### 2026-11-21 14:29 · apron 证据可证性复核

Game03 子仓仍为 dirty 的 codex/optimize-game03，HEAD 4f44d02d809939e8e9406a74d046c1e3fb974da1；本轮未改 Unity 源码、未运行 Unity、未回拷任何隔离候选。最新既有 XML 时间为 13:17–13:19；PlayMode 捕获 1/1 Passed 仅证明用例执行，不能证明接缝门通过。

复查当前参与运行 SHA 对应的捕获源后，GLM 记录中的“20/35/60/90 mm 四档全记录”与磁盘实现不一致：实际代码只用固定 60 mm 偏移、0.1 m 采样、单次 Physics.Raycast；样本结构未记偏移。偏移归属和单次最近命中不足以区分目标面及重叠表面；主缝 d=0.08/0.28/0.58 m 的有效 Road↔apron 高差约 27.9/21.5/11.9 mm，d≥0.68 m 已命中 Paved sidewalk。stepOutsideTransition=0 因此不是剩余边界平齐证据。侧缝探针的实际 owner 顺序与分类器预期相反。

报告中两个 stepAtCorner 字段仍是默认零；对应局部变量没有写回。maxTriangleAreaError 也在正面积分支直接归零，没有比较面积。上述零值均不应作为量测结论。已在 GLM 任务记录追加独立审计更正，不更改源图、截图、XML 或测试代码。

修正该输出目录的 SHA 清单以排除清单自引用后，当前 38 个非清单文件均逐项匹配；之前清单把自身记成空文件 SHA 的条目无效，现不再保留。另更正 macro 机位文字：manifest 的相机 eyePosition.y=0.8 m，不是 0.06 m；依旧不是 1.77 m 玩家视点。

本轮按 gpt-6-luna / max 请求三路并行完成只读视觉、几何和课程审查，但协作界面不公开运行模型 ID/推理档，故不宣称已遥测核实 Luna Max。评审共同支持的顺序是：先让 GLM 修复隔离副本的 owner-aware 测量与有效性门，再按可信数据返修几何；之后对第 4–6 阶段补真正课程入口实走。人眼视角最突出建模差异仍是 S03 路口右侧灰色端墙；接缝实验没有处理该立面。

ZCode 当前窗口可见模型选择标签 glm-5.3-flash / 最高，但本轮任务输入框始终未取得焦点、发送按钮禁用，实际工作包未送达；该选择标签不能证明后端身份或 GLM 工时占比。Chrome Game03 ChatGPT 规划会话已显示旧回复，但前景 Windows 文件选择对话框阻断新消息，本轮没有新同步。未将上述阻塞记为 GLM 执行或网页评审完成。

下一步只在能确认 GLM 输入可用后派发：独立复制当前脏工程、修复四档签名探针与 owner 分层记录，消除默认零及假面积指标；分别产出 EditMode 与 seam capture 原始数据，保留代码 SHA 和全量校验。新数据确认几何门后，再把可逆的 S03 端墙立面细化作为最高视觉收益项，以同机位 1.77 m / FOV 76°前后图验证，并检查信号灯、横道与转弯车辆视线。未有这些证据前，不把接缝或街景真实性标 PASS。

方法依据：[Unity 2022.3 Collider.Raycast](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Collider.Raycast.html)、[Physics.RaycastAll](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Physics.RaycastAll.html) 支持按目标 Collider 查询/收集命中，但不自行定义本项目的门槛；美国 Access Board [PROWAG R304.5.4](https://www.access-board.gov/prowag/technical.html) 对其适用范围内的 curb ramp / blended transition 表面不允许 level changes，可作技术对照，不能替代中国适用规范判定。项目 1 mm 与 3.2% 均是内部验收口径，不是法规结论。

### 2026-11-21 16:32 · 接缝量测门与 Stage 5 观察目标

Game03 子仓仍为 dirty 的 `codex/optimize-game03` / HEAD `4f44d02d809939e8e9406a74d046c1e3fb974da1`；本轮未修改游戏源码、未运行 Unity，也未把隔离候选回拷。当前捕获测试源 SHA `4536c357209b11888973cc59839d3fd17ad1aa71f2bd8ea4dbd8dd61bc34afab`、几何源 SHA `3c1157fe65a6f102b935c1fd6e4ddd4456f4f4203a1a11af43d1f8bd4e91982e`、`StreetWorld.cs` SHA `6bfa8a71755cac0f7d3455f1cb482c7834692ae00a4e1f27f9f9aea6d7d6f884` 均与上一候选一致。

捕获器仍以固定 0.06 m 偏移、0.1 m 沿边采样和单次 `Physics.Raycast` 取最近面；每条样本没有 offset/world XZ。侧缝实际 Road↔apron hit 顺序与 classifier 相反；主缝远端探针命中 `Paved sidewalk`，所以 `stepOutsideTransition=0` 没有证明那段露出路面且平齐。两个 corner step 没写入报告；非退化三角的 `maxTriangleAreaError` 直接设为零。旧 PlayMode 1/1 只能说明捕获用例执行，不是 owner/step 的验收通过。下一次可信量测应按 road、apron、sidewalk、kerb 分 owner 记录，分别作指定 Collider 命中与全场景命中，报告 planned/matched/missing/wrong-owner/occluded；零有效样本、owner 不符或漏样均应失败。Unity 2022.3 文档说明 [`Collider.Raycast`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Collider.Raycast.html) 只检查指定 collider；[`Physics.RaycastAll`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Physics.RaycastAll.html) 返回全部命中且顺序未定义，故不可依赖首个/最后一个结果。

课程审查发现 Stage 5 文案（`StreetCourse.cs:19`）承诺换位后“看清转弯车流”。`DirectionVisible` 的主采样（`StreetGameManager.cs:378–399`）按直行车道和 `RoadTargetsForLane` 判断；活动车辆 fallback 确实可能认可转弯车，但要求其当前 Z 接近某直行车道中心并在所观察方向前方。场景没有独立 turn-route targets；既有遮挡用例 `S03OpaqueKerbShelterRequiresSafeForwardRepositioning` 检查的是 `LeftRoadTargets` 直行点，风险刷新用例也只移动直行车。11 月 20 日 `Realism-BusStop-20261120/bus-play.xml` 的 14/14 含 S03 普通过街/遮挡用例，但它们走 `StartPractice()`，不能证明课程入口的 Stage 5/6 通过。准确缺口是课程文案与显式目标/验收覆盖不完全一致，不是转弯车被彻底忽略。

课程专项目前只校验阶段/任务映射；观测刷新测试虽循环检查课程阶段标志，真实走课仍调用 `StartCourse(2)`。`S03T3AllThreeRoundTripConfigurations` 用真实玩家输入完成双段，但从 `StartPractice()` 进入，未覆盖课程模式返程中长时间等待或车流改变后旧观察失效。最小课程 E2E 包应分别从 `StartCourse(3)` 实走 Stage 4 四车道观察与过街，从 `StartCourse(4)` 沿人行道换位并核验转弯轨迹观察证据，从 `StartCourse(5)` 完成去程/返程并在返程中核验观察清空、车流或相位变化后的重新观察与安全判定。每例绑定工作树源 SHA、事件日志与 XML；Stage 5 截取遮挡前后固定 1.77 m 机位，Stage 6 分段记录去返程。现有 `CourseObservationRefresh-PlayMode.xml` 5/5、`compat.xml` 9/9、`bus-play.xml` 14/14 均没有本轮工作树源 SHA，分别不能替代 Stage 4–6 的完整课程证据。

另一个需明确的时效边界：课程测试的 `WaitForSafeEntryWindow()` 会等 WALK 剩余时间达到 `SafeCrossingSeconds`，而入场判定目前只检查 `phase == Walk`（`CrossingEvaluator.cs:74–82`）；现有课程用例没有隔离“观察后仅信号倒计时变短”或 WALK 末段进入的情况。新增课程门应分别记录观察是否过期、当前相位/剩余时间、车辆变化、入场许可和安全完成结果；若仍将剩余时间交给玩家判断，应加入可复现的末段配置及行为验收，不以测试 helper 的等待条件推断运行时门槛。

下一项课程验收应从 `StartCourse(4)` 真实换位开始，覆盖至少一条转弯 path 在遮挡前后的位置、视线目标、记录的观察证据与过街判定；静态目标可见、场景无遮挡视线和动态车流中可见须分层记录。其余 Stage 4/6 入口用例按上面的三段包独立验收。

本轮派发了三项只读 Luna/max 配置审查，但协作槽位中遗留的 `/root/facade_capture` `pending_init` 会话仍占位，无法同时维持三名可核验并发；工具也未公开实际运行模型/推理档遥测。ZCode 显示模型选择为 `glm-5.3-flash / 最高`，但本轮工作消息未能输入/发送，实际 GLM 主动工作为 0 分钟，不计作达到 50%。Chrome 网页 ChatGPT 评审草稿已写入 composer，但页面模型控件操作返回 `coordinate input geometry is unavailable`，输入未发送，故没有本轮网页答复。暂定顺序是先修隔离副本捕获器并建立可信 owner 数据，再补 Stage 5 转弯路径观察验收，之后实现 S03 南侧住宅端墙的可逆立面细化；端墙方案待固定 1.77 m / FOV 76°机位实拍后再定像素门，源码投影估算不得冒充 Unity 图像。

### 2026-11-21 17:43 · 接缝运行证据、课程入口与 GitHub 资产复核

本轮只读审计未运行 Unity、未改游戏源码、未回拷隔离候选。Game03 子仓仍为 dirty 的 `codex/optimize-game03` / HEAD `4f44d02d809939e8e9406a74d046c1e3fb974da1`；Unity Editor 进程未运行。

#### 接缝量测证据

- GLM 前一批 13:17 的 `after/editmode-recheck-20261121T1315.xml/.log` 确实存在；筛选 `StreetTurnShoulderTests` 为 1/1 Passed、asserts=0（XML SHA-256 `f7fae5a40fe803dc0f997ddd6c91cef31f64352d158f47204ffabe74bd693658`）。13:19 的 `after/seam-recheck-20261121T1315/capture-tests.xml` 也为 1/1 Passed、asserts=0（XML SHA-256 `5a6e95ccc8b2c0ef20d68a62020a9c828544cdd6898d8b90a08f584ffccfd7b4`；manifest SHA-256 `1e90461fd748d4d51b14ee2bad56a7239ced23d109e698c376fc1a033504a72d`）。它们证明筛选用例运行完成，不是接缝整体 PASS。
- 对应 manifest 14 个主缝点中仅 6 个是 `Road asphalt ↔ Turn shoulder asphalt` 有效 owner 配对，高差从 27.9 mm 递减到 11.9 mm；其余 8 个命中 `Paved sidewalk`。侧缝 19 点没有一个符合源码预期的 owner 方向（18 点 `Turn shoulder asphalt → S03 intersecting side street`，1 点 sidewalk → side street）。
- 实际采样器仍是固定 60 mm 偏移和 0.1 m 步长；manifest 没有样点偏移或世界 XYZ，`sourceSha` 是描述字符串而非提交树哈希。两个 corner step 字段没有从局部累加值写回；`maxTriangleAreaError=0` 未真实计算。隔离运行日志没有列出测试副本逐文件哈希，因此当前参与源 SHA 不能完全绑定到 Temp 项目。结论：捕获工具可信度仍未过门，接缝几何维持 NOT PASS。

#### 课程验收缺口

当前没有完整的 `StartCourse(3/4/5)` 课程 E2E：Stage 4 是 S02-T2 单段、Stage 5 是 S03-T2 单段、Stage 6 是 S03-T3 双段。已有课程相关用例只检查映射/标志后返回菜单，实走与返程用例从 `StartPractice()` 开始。Stage 5 主观察目标仍检查直行车道点；车辆 fallback 仅在活动车当前 Z 落入直行车道中心 ±0.825 m 时可能被记为普通来车，没有独立转弯轨迹目标或带 routeKind 的观察事件。因此不能称“完全忽略转弯车”，也不能说已证明玩家看见了真实转弯路径。

#### GitHub 街景资源初筛

| 仓库 / 固定版本 | 可复用部分 | 不直接导入的原因 |
| --- | --- | --- |
| [Mapforge `3c633fc`](https://github.com/stonyp/mapforge/tree/3c633fc0d5f64f0807e23a0496fd55f9f443f9fe) · MIT 代码 | 从 OSM 生成有宽度道路、人行路径、建筑体量并导出 GLB；适合重建街区空间关系。 | 共用少量纯色材质，README 把分类立面纹理列为 roadmap；不能解决精细门窗/店招/墙面质感。OSM 数据的署名与数据许可需另行核对。 |
| [3DStreet asset source `23d43bc`](https://github.com/3DStreet/3dstreet-assets-source/tree/23d43bc372caabde633b278738dafb42123f031a) | Blender 源模型、共用材质/纹理与 GLTF/GLB 导出流程；内容主题贴近街道设施。 | 默认模型为 CC BY-NC-SA，部分资产另有第三方许可；逐项许可未核清前不导入。 |
| [procedural_buildings `d3ed8d3`](https://github.com/wsparcie/procedural_buildings/tree/d3ed8d34c92e1402242055bac6ea366e8b8b382d) · MIT | Blender 3.6 Geometry Nodes 有公寓/排屋等程序化立面轮廓，可作原型参考。 | 项目自述为简单预设，需实际近景渲染才知道细节是否够用；并非 Unity 成品包。 |
| [kit-cidade-lowpoly `d9de7e2`](https://github.com/kayquer/kit-cidade-lowpoly/tree/d9de7e25d476d8b8572971840b129c28a68e9cf7) · CC0 | 129 个 GLB，街道家具/树/路块较完整。 | README 明确是 toy-scale low-poly、单色无贴图，和真实性目标不匹配；只可考虑做占位道具。 |
| [Codriver 3D assets `e961933`](https://github.com/codriver-io/codriver-3d-assets/tree/e9619339dd3d74d81cb87302e4692082e0c3dbff) · 模型 CC BY 4.0 | 真实地标的多档 GLB 可作比例和结构参考。 | 资产以地标和桥为主，不是普通住宅、沿街商铺或路口生活设施；数据源/署名需随资产 manifest 核对。 |

本轮没有找到同时满足“普通城市街道建筑、细节较真实、Unity 可复用、许可可直接确认”的即插即用包。下一步优先由 GLM 在隔离副本修 owner-aware 捕获器并重采，再补 `StartCourse(3/4/5)` E2E；之后再依据固定机位截图定制或复用端墙、商铺、信号灯、路缘等视觉资产。资产导入前必须做固定 Commit、逐项许可、材质/贴图依赖和 Unity 近景渲染核验。

### 2026-11-21 20:35 · 固定眼高端墙候选复核

- 自动化 `game03` 已核实为 `ACTIVE` 且 `FREQ=HOURLY;INTERVAL=1`，当前已经每小时运行，不需另建重复任务。
- 本时段检查了隔离副本 `C:\Users\Ait\AppData\Local\Temp\game03-endwall-hourly-20261121T1912\BuildOutput\EndWallRealism-20261121T1912\`。目标是 S03 近侧 `Facade Residence` 西向山墙；候选增加窗框/凹槽玻璃/窗台、服务门和门罩、勒脚、落水管，均为无碰撞体的视觉部件。固定 eye height 1.77 m、FOV 76°；前后 manifest 机位相同，场景 Collider 数均为 91。
- 两份 `S03EndWallEvidence` XML 各显示 1/1 Passed，但 `asserts=0`；因此只记为捕获 smoke，不证明断言、真实性或整体验收通过。manifest 未写入实际源码 SHA，`participant-sha256.txt` 只有候选生成器与新增测试文件 SHA，图像与其运行源码尚未形成完整可复现绑定。ZCode `report.md` 所称 collider gate 不据此认定已通过。
- 临时生成器 SHA-256 `A4223A592BEF358E2C3E6B3CF3FFEEBECFF669ABA554E7DE1E2A867DE590D43D`；共享工作树相同文件 SHA-256 `606525A6CE01B7188B57626C017B02A2C92195514B995F0F4CC0045511FCE4EB`。本候选未复制回脏工作树、未集成，也未启动 PICO/头显验证。`report.md` 中 GLM 任务自述约 36 分钟；ZCode UI 模型/档位显示 `glm-5.3-flash / 最高`，但无 provider 或计时遥测，不能独立核实模型、耗时和 GLM 工作占比。
- 三路只读审查按 Luna/max 配置并行请求并已收到结果；工具未提供实际模型/推理档遥测，故不记为身份核验完成。Chrome Game03 规划聊天已同步证据并询问下一步；界面显示 Pro，提交后出现用量限制提示，没有新答复，本轮没有网页决策或新增权威/顶会引用。
- **下一门：**仍在隔离副本补齐 before/after 源码哈希、PNG 哈希、Unity 版本/命令/退出码，并让测试对目标对象、Collider 数和必要可视细节产生实际断言；完成固定眼高画面审阅后才评估是否移植。此候选与接缝及 Stage 4–6 课程验收均不构成 PASS。

### 2026-11-21 21:40 · proof-gate 隔离复核进行中

自动化 `game03` 已复核为 ACTIVE、每小时一次，无需新建重复任务。ZCode 的 follow-up 已提交，界面显示 `glm-5.3-flash / 最高` 并正在执行；首个可见步骤是比较共享树与临时副本文件清单/根指纹、再检查旧候选与新隔离副本差异。该标签不是 provider 遥测，实际模型与占比仍待核实。隔离输出尚未由本地独立审计，因此没有新的测试或视觉 PASS。

本轮目标坐标已纠正为 S03 `side=-1/bay=0/x=0` 的 Residence（`z≈-14.6`），其西向山墙是 `local +x → world -x`；`x=13/bay=1` 是 Pharmacy。旧候选的新框局部 `x≈5.13` 被墙面 `x=5.15` 遮住的风险仍需在实际图中核验。Chrome 规划聊天持续显示用量限制，未获新答复；三份 Luna 只读审阅没有可核验模型/max/并行运行元数据。共享脏工作树只读，未移植候选。

**待验收：**确认隔离源树指纹、限定代码差异、目标层级/位置/旋转、baseline/enhanced 模式、完整 Collider 层级相同且细节无 Collider；每份 PNG/XML/log/manifest 记入不自引用的 SHA 索引和实际进程退出码；用 1.77 m / FOV 76°固定机位确认变化位于 `x=0` 山墙且不改动其他区域。任一证据缺失就保持 FAIL/待定，不回拷。
### 2026-11-21 22:57 · S03 端墙隔离候选 final5 复核

`game03` 自动化已核实为 ACTIVE、每小时一次。ZCode 在 Temp 隔离副本完成 S03 `x=0,z=-14.6` Residence 西向山墙的 final5 复跑：固定机位记录眼高 1.77 m / FOV 76°，两张 1920×1080 PNG 的独立差异框为 `(1104,196)-(1331,499)`；目标网格顶点从 4758 增至 6100。JSON 检查记录目标完整路径且精确匹配数为 1；Collider 路径与类型清单由测试逐项比较，before/after 均 91 条且匹配；两窗暗像素检查通过。before 4/4、after 6/6 JSON 检查通过，XML 各为 1/1 Passed（交付报告称退出码 0）。独立重算 evidence index 时发现并移除了其自哈希项；剩余 62 个外部文件哈希全部相符。

候选 Builder SHA-256 `a7b4438781344a676f32dc185c259889d7c1ddeae5418dbdcd0aebb0cdcc1ae4`；测试 SHA `839bf9d48dc03595699b6b40b07e6b2517e2eea183ee52892d39a8ea1e3f2d59`。共享 Builder 实测 SHA `0E69914135A8D04F4BCC97147940CC01DA5A4091F3B7B9D9A9E7C6BA38E4FBA6`；本轮未写共享工作树，未回拷/集成/提交。before/after 源树 SHA 不同，交付报告归因于 Unity 序列化改写 `.mat/.asset`，所以不能据此声称整树一致。

**验收边界：**这是隔离候选的结构和固定机位画面证据，不代表街景真实性或 Game03 整体验收 PASS。仅有表面窗框/玻璃/窗台层次，未做真实墙洞；没有 PICO/头显、夜雨、S01/S02 或课程 Stage 4–6 实测。下一步由网页规划会话评估是否继续丰富真实构造并给出可验收任务包；目前该会话仍显示用量限制。三份 Luna 只读结果已收到，但实际模型/max/并发没有遥测核验。ZCode 显示 `glm-5.3-flash / 最高`，但实际后端/工时比例不可核验。证据目录：`C:\Users\Ait\AppData\Local\Temp\game03-endwall-prooffix-20261121T2215\BuildOutput\EndWallProofFix-20261121T2215`。

### 2026-11-21 23:52 · 三路端墙建模复核

按用户要求并行派发三项只读审查，覆盖墙体几何、真实课程视点和证据独立性；报告已收齐，但协作工具没有可核验的实际模型、max 档或并行遥测。三路结论一致：final5 仍是未集成的 Temp 候选，窗户没有切穿墙壳；候选 plain 不是当前共享脏工程基线；固定墙外评审机位不能证明玩家路线可见。共享 `StreetDistrictBuilder` 当前仍有未受 S03 profile gate 限制的旧 `EndWallDetail`。

Stage 5 的 `StartCourse(4)` / S03-T2 从住宅附近朝 `+Z` 离开，端墙在身后偏左；Stage 6 的 `StartCourse(5)` / S03-T3 返程主要面向住宅正立面。这是从源码路线与几何得出的推断，未运行场景，不能断言任何时刻都不可见。**下一步：**先从当前共享 dirty 工作树创建隔离快照并绑定源码/资产指纹；用真实 Stage 5/6 玩家相机捕获路线视点，确认端墙进入视野后，才做 S03-only 真开洞与窗膛。玻璃后退量至少 80 mm，洞口用墙壳缺面或穿洞射线证明；S01/S02 目标层级、网格和画面保持不变，Collider 完整路径与类型清单保持 91 项一致，before/after 源与图像均有 SHA。路线不可见、profile 外溢、窗洞仍被墙遮挡或几何仍是贴片，都停止并保留隔离候选。

本轮 Chrome 规划输入受用量限制和 Computer Use 状态错误影响，未发送评审问题，未取得网页答复。ZCode 当前选中的模型标签为 `glm-5.3-flash / 最高`，但停留在 Game04 对话；尝试切换 Game03 时 helper 返回 `already has an active request`，未能派发 GLM 实作。本轮没有工程修改、测试或 PASS，不计作达到 GLM 50% 工作量要求。

### 2026-11-22 00:12 · S03 课程可见模型优先级

按用户要求并行派发三份只读审阅，覆盖建模优先级、隔离实现候选与独立渲染/碰撞验收。请求指定 `gpt-6-luna/max`，但协作工具无实际模型身份、max 档或并行时段遥测，故只记录派发和交付，不把请求配置记为验证结果。

**优先顺序更新：**本轮先暂缓住宅西山墙双窗。旧 approach 图虽将端墙显示得很大，但它不等于 `StartCourse(4/5)` 的真实玩家视点；源码推算 Stage 5 从住宅朝 +Z 离开时端墙在身后偏左、Stage 6 返程主要面向住宅正面，仍需实走截图验证。审阅建议先评估 S03 公交站亭及其遮挡牌：Stage 5 的教学目标包含被站牌遮住左近车道后沿人行道换位观察；现有站亭由大块不透明背板、顶棚和立柱组成，在旧近景里更像封闭牌板。可在保持遮挡面及位置的前提下，改善金属框架、可透视侧部、座椅和站名/线路信息的可读性；不得侵入盲道/过街入口，不改变目标车道的原遮挡和换位后视野。

**运行验收门：**从 `StartCourse(4)` 实际玩家机位捕获站亭前及沿人行道换位后的图像，证明目标左近车道先被遮挡、换位后可见；取 `StartCourse(5)` 去程与返程玩家视点图，确认新结构不误导另一阶段；检查盲道/横道净空和角色/交通视线，逐项审计站亭碰撞体路径、类型、启用状态及世界几何。只有这些真实场景证据到齐，才在新隔离候选实施。端墙需先用 Stage 5/6 路线图确定实际投影大小后再排序。当前无本轮模型修改、Unity 渲染、课程事件轨迹或碰撞回归，故没有任何视觉或训练效果 PASS。

来源边界：FHWA《Pedestrian Safety Guide for Transit Agencies》，Dan Nabors 等，2008，FHWA 明示该指南不是标准、规格或法规；适用于公交站点行人安全审查背景，不提供本项目模型尺寸阈值（[官方指南](https://highways.dot.gov/safety/pedestrian-bicyclist/pedestrian-safety-guide-transit-agencies)）。NACTO《Transit Street Design Guide — Small Transit Shelter》，2016，建议开放车辆视线、透明材料、座椅与路线信息，并保持无障碍通道；是北美设计指南，不能替代中国规范（[原文](https://nacto.org/publication/transit-street-design-guide/station-stop-elements/small-transit-shelter/)）。Kim 与 Torrens，NYU，ACM SIGSIM PADS 2024，报告 22 人道路过街 VR 演示中低分辨率图形仍获得较高 presence 和自然行为；其小规模演示不证明 Game03 教学有效，也不意味着可忽略站亭的可识别性（[论文 PDF/DOI](https://doi.org/10.1145/3615979.3656060)）。这些来源已通过官方/原始出版页面复核；策略仍需 Chrome 规划聊天评审，本轮由于桌面输入故障未能发送。

新隔离快照 `C:\Users\Ait\AppData\Local\Temp\Game03-S03-RealWindows-20261122T001249` 的 633 个文件和 17,440,034 字节经独立复算全部匹配；它仍是未编辑源副本。Chrome 读取到上一条关于真实双窗洞的旧答复，但本轮点击/输入操作失败，未提交公交站亭证据，也未获得更新决策。ZCode 仍在无关 BCI 任务，没有 Game03 GLM 交付，本轮 GLM 实质工时为 0，不能声称达到 50% 工作量要求。共享工作树未改，未运行 Unity/PICO，未新增测试或 PASS。


### 2026-11-22 02:12–02:20 · 正式课程视点先于建模

本轮三项并行Luna/max只读复核已收齐：Stage5/6课程路线、现有E2E测试缺口、建模候选/回归风险。工具无provider、max档或并发遥测，模型身份仅记为“请求gpt-6-luna/max”，不冒充已核验。

**源码证据与结论：**StreetCourse.cs:18-20映射Stage5 index4→S03-T2（站牌遮挡后换位观察）、Stage6 index5→S03-T3（咖啡店往返）。StreetMissionAcceptanceTests.cs:155-172对后几个课程只断言映射/段数后回菜单；同文件完整任务回放调用StartPractice（:101-125、:174-182）。CourseObservationRefreshTests.cs:182-199也只验证配置后退出；真正完整的观察路线集中在Stage3（StartCourse(2)，:201-266）。站牌遮挡验收在StreetAcceptanceTests.cs:108-130使用Practice，不证明正式Stage5有真实转弯车可见。
StreetGameManager.DirectionVisible遍历scenario.StraightLaneCount（StreetGameManager.cs:378-399）；转弯车只用于风险签名（:354-362），不要求“实际看见转弯车”。由此推断课程目标和观察判定未对齐，仍需正式StartCourse(4)验收证实。
StreetWorld.cs:413-477已建站亭棚顶、框架、立柱、座椅、线路板等构件，下一轮不要重复堆站亭装饰。S03西山墙双窗候选有实际开洞/碰撞证据但只有墙面检查视角；新墙网格漏UV0，呈现会失去灰泥纹理。若Stage5/6玩家视角证明这面墙值得保留，先补UV再重拍。课程截图没完成前暂停新街景建模。

**Chrome规划端裁定：**在专用规划聊天收到当前证据并选定唯一优先项：先建立CourseStage56E2E，真实走StartCourse(4)/(5)，不先修run14完整Collider基线。课程视点取证前冻结新增街景建模。阶段门：每stage两个固定seed、每seed回放两次（共8次完整课程）；Stage5同一真实转弯车记录车身进入视锥、实际无遮挡像素、玩家相机方向、首次可见与进入冲突区时刻；Stage6记录咖啡店去返两段、合法接续和终态。不能以Practice、直行车、纯Collider射线代替。课程视图不可见转弯车/运行不完整/种子不能重放时先修课程或遥测，停止美术。
网页当前选项显示“极高”；规划答复自称GPT-6，但页面及协作工具不提供独立模型/服务端推理元数据核验。

**GLM派发及巡查：**已在ZCode建立新任务并交付CourseStage56E2E包，任务要求GLM负责至少70%实质工作、Codex最多30%静态/证据审查。新证据限定于C:/Users/Ait/AppData/Local/Temp/Game03-CourseStage56E2E-20261122T021213，源为只读的既有冻结快照；不动共享脏树/STATUS/V5/正式宿主、不覆盖Unity、不复制Library。ZCode聊天当前界面可见glm-5.3-flash / 最高，真实provider与工时不可验证。启动后首步只读核验source manifest；本记录时尚无代码、测试或可验收交付，不能计入GLM实际占比。
**run14独立问题：**enhanced dump 1/1 Passed，但XML asserts=0；baseline因TerrainPhysicsModule、VehiclesModule缺失而编译失败，不能得出对照结果。保留原始失败，不扩大到完整Collider稽核来推迟Course E2E。
**本轮状态：**只读源码审查、异步派发与证据核对；未改游戏代码/模型，未运行本地Unity/PICO，无新测试或PASS，无课程路线截图。STATUS_CURRENT.md已同步记录。



### 2026-11-22 02:20–02:40 · Stage 5/6 Course E2E 实施状态

本轮按要求派发并收齐三项 Luna/max 只读审查，身份与推理档只有请求值，没有运行遥测。Chrome 专用规划聊天收到路线/课程缺口和历史建模证据后，优先级是先完成 StartCourse(4)/(5) 正式路线遥测，暂停新增街景模型；其网页界面显示“极高”，实际服务端模型无法独立核验。

ZCode 专项任务隔离到 Temp：已创建 CourseStage56E2ETests.cs 与 run_e2e.py，界面显示 glm-5.3-flash / 最高；GLM 分工预算至少 70%，当前实质占比仍待结果核算。第一次 Unity 导入编译因 PICO OpenXR GitHub 包连接重置失败；从本机已锁定的包缓存复制到 Temp LocalPackages 后，第二次编译退出码仍为 1，错误是新测试文件第 100 行 Object 类型名歧义。Unity 进程已退出；尚未启动课程用例，没有 E2E XML、运行遥测或路线截图。该状态不构成视觉、教学效果或关卡验收 PASS。

下一门槛：只在 Temp 测试副本修正编译错误，确认 Unity 自然退出码为 0；先单跑一个正式课程用例以验证 harness，再执行每阶段两种子、每种子两次的八个完整回放。分别记录 Stage 5 实际转弯车、车身可见像素、相机朝向、首次可见/冲突区时刻；Stage 6 两段去返程及合法接续。未完成前继续冻结建模，不回拷共享脏工程。

**02:40 更新：**GLM 已在隔离测试文件中修复 UnityEngine.Object / System.Object 命名歧义。第三次 Unity 批处理导入编译日志显示 4.93 秒脚本编译、自然退出码 0、无 C# error。该证据只证明新增测试程序集可编译；目前 evidence/runs 仍为空，还没有 Stage 5/6 正式课程运行、XML、遥测或截图。继续等待单例试跑和八次课程回放，本轮不据此报视觉或课程 PASS。

**02:48 Stage 5 A1 回放更新：**首次正式课程试跑 XML Failed（runner 自然退出码 2，约 52.5 秒），不是已确认的课程/模型失败。S5-A1 遥测记录 518 次转弯车取样、96 次进视锥、43 次朝向目标、37 次处于冲突区，但 0 个像素可见样本。代码复核发现 SampleTurningCar 在 full 和 withoutCar 两次渲染前均关闭车辆 Renderer，导致像素差分通道失效。失败证据已留在 Temp evidence/runs/S5-A1；Codex 将根因与修正门写入 Temp/notes/CODEX-REVIEW.md。先修采样器、恢复原始 Renderer 状态并做可见/遮挡对照片段，再重跑 S5-A1；此失败不授权修改模型，也不支持真实遮挡结论。

**02:55 二次验收：**采样顺序修复后的 S5-A1 用例 XML Passed，runner 退出码 0；74 个转弯车样本中 26 个有渲染像素差。RightTurn encounter 5 在 worldTime 20.2403 首见、22.0003 进入冲突区且完成路线。可是 summary 中 waitPointOcclusionProven=false；原等待点 encounter 6 已经可见 56,614 像素，而换位后可见的是不同车辆。测试还未要求首点遮挡，也未要求同一车辆被挡后再显现，因此 Stage 5 的遮挡学习目标没有通过。像素助手当前也未在 finally 恢复 Renderer.enabled 原值；run-meta 仍缺输入源码/测试/runner/manifest 哈希。已把这些作为新阻断写入 Temp 评审记录；暂停剩余七次回放和建模，先修验收门，不能由单例 Passed 推导课程或模型通过。

## Codex follow-up review — S5-A2 and S5-B1 (2026-11-22 03:01 CST)

- S5-A2 (SeedA Rep2) XML Passed, natural runner exit 0, but it reproduces the Stage 5 learning-gate miss: `waitPointOcclusionProven=false`, original-point contrast encounter 6, repositioned evidence encounter 5. The latter was first pixel-visible at 20.0403, reached conflict at 21.8003, and completed its route. This supports only that a turning car can be seen after repositioning; it does not show the same car blocked then revealed.
- S5-A2 `summary.json` is invalid JSON: `evidenceCarRoute` is emitted as bare `RightTurn` (unquoted). Do not rely on this summary as machine-readable evidence until the serializer is fixed and output reparsed.
- S5-B1 (SeedB Rep1) XML Failed, natural runner exit 2, with `Both real observations must precede entry.` at CourseStage56E2ETests.cs:583. No summary was emitted. This proves the harness/course did not satisfy that required observation-state assertion in this run; inspect the observation sequence and actual course state before retrying. Do not suppress or weaken the assertion to make the test green.
- Current visible run set is S5-A1 Passed-at-runner-level but lesson gate unmet, S5-A2 same lesson gate unmet, S5-B1 Failed; 3 of planned 8 cases appeared, 1 runner failure, and no Stage 6 run. Stop the matrix and keep art frozen until the same-car occlusion transition, observation-state failure, valid JSON output, renderer-state restoration in `finally`, and run input hashes are addressed.
- Existing Chrome planning decision still applies: formal Stage5/6 Course E2E before more art. This new evidence has not yet been reviewed by the Chrome ChatGPT planner; the Chrome window's native file dialog has blocked direct screenshot upload. Any follow-up through the same ChatGPT conversation connector must be recorded as connector messaging, not represented as a Chrome UI interaction or screenshot review.
- No shared game source, model, or formal host was changed. GLM made substantive code/test/runner work in the isolated Temp project; ZCode UI showed `glm-5.3-flash / 最高`, but actual model backend and work-share percentage are not independently instrumented.


### 2026-11-22 · Course E2E 新证据及建模门更新

最新 S5-A1 当前测试哈希 `0D974BA0AF3A33DA17612F8C6CDE0D45E021B0ED05DEB2B4231E205F04B8DB22` 的正式课程运行仍失败（Unity 自然退出码 2），原因是左侧 fresh observation 在约 33.0006 被 `left-road-risk-changed` 作废，之后仍以 `leftBefore=False; rightBefore=True` 启动穿越并触发 UnsafeArrival；无 summary.json，不记 Stage5 PASS。换位后同一 encounter 车辆从 22 个像素差异增长至较大区域只证明局部显现；首帧车辆不可辨，差异像素阈值不得代表教学可读性。A2/B1 旧运行与当前输入不同，尚无 Stage6；共享模型继续冻结，等待正式课程路线与观察状态验收。

隔离 RealWindows Run15 审阅发现山墙网格未写 UV0，却使用带贴图的灰泥材质；固定开发机位对照出现纹理退化。仅作为候选修复线索，尚未在共享模型或正式课程视点复现。模型若解冻，应先证明课程路线中该面可见，再修 UV0 并做同机位纹理/碰撞回归。运行证据需记录唯一 run ID，并将 runner 与源快照清单哈希纳入输入哈希；当前 S5-A1 同名目录覆盖风险、缺 summary、缺哈希绑定均待解决。

