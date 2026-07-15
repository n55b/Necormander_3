/// <summary>
/// 연결(드래그) 없이 신호를 주고받는 통로. 어디서든 한 줄로 쏘고, 어디서든 듣는다.
///
///   쏘기:  Signal.Fire(ShakeSignal.플레이어피격);
///   듣기:  Signal.Listen&lt;ShakeSignal&gt;(내함수);      (OnEnable에서)
///   떼기:  Signal.Unlisten&lt;ShakeSignal&gt;(내함수);    (OnDisable에서 반드시)
///
/// 신호 종류(enum)마다 통로가 완전히 따로 논다. FadeKind과 ShakeSignal은 절대 안 섞이므로
/// 페이드 드롭다운에 "플레이어피격" 같은 게 뜨는 일이 없다.
/// 그러면서 셋(페이드/UI쉐이크/카메라쉐이크)이 전부 이 같은 모양을 쓴다 — 하나 익히면 나머지도 똑같다.
///
/// ★★ 언제 신호를 쓰나 — 이거 하나만 지키면 된다:
///
///     드래그로 이을 수 있으면 신호를 쓰지 마라. 신호는 드래그가 '불가능한' 곳에만 쓴다.
///
///   드래그가 안 되는 곳 (= 신호가 필요한 곳):
///     - 문은 맵 생성 때 코드로 생겨서 씬에 미리 없다 → 에디터에서 드래그할 대상이 없음
///     - 플레이어도 런타임에 생기는데 체력바는 씬에 미리 있음 → 서로를 못 찾음
///     - 검은 커튼은 씬이 바뀌어도 살아남음 → 씬에 묶인 참조로는 못 가리킴
///
///   드래그가 되는 곳 (= 신호 쓰면 오히려 나쁨):
///     - 보스방 스포너가 페이드를 켜고 싶다 → 둘 다 그 방 안에 있다. 그냥 드래그해라.
///       신호로 하면 "무슨 일이 일어날지"가 보스방을 떠나 커튼 프리팹으로 흩어진다.
///       연출은 그 연출이 벌어지는 곳에 모여 있어야 한다.
///
/// 신호에 인자를 싣지 않는 이유도 같다. 인자로 뭘 가리킬 수 있으면 애초에 드래그가 되는 상황이고,
/// 인자를 실으면 받는 쪽마다 "이거 나랑 상관있나?"를 판단해야 해서 '듣고 그냥 반응한다'가 깨진다.
/// 구분이 필요하면 인자가 아니라 신호를 하나 더 만든다 (플레이어피격 / 적피격처럼).
///
/// 덤: 드래그 배선은 씬 파일에 묻혀서 "누가 이걸 부르나"를 못 찾는데, 신호는 이름으로 검색된다.
/// </summary>
public static class Signal
{
    /// <summary>신호를 쏜다. 그 순간을 아는 쪽이 부르면 된다 — 인스펙터도, 중계자도 필요 없다.</summary>
    public static void Fire<T>(T signal) where T : struct, System.Enum => Bus<T>.Fired?.Invoke(signal);

    /// <summary>신호를 듣기 시작한다. 반드시 OnEnable에서.</summary>
    public static void Listen<T>(System.Action<T> handler) where T : struct, System.Enum => Bus<T>.Fired += handler;

    /// <summary>듣기를 그만둔다. 반드시 OnDisable에서 — 안 떼면 죽은 오브젝트가 계속 반응한다.</summary>
    public static void Unlisten<T>(System.Action<T> handler) where T : struct, System.Enum => Bus<T>.Fired -= handler;

    // 제네릭 클래스는 T마다 static 필드를 따로 가진다 → 신호 종류별로 통로가 저절로 분리된다.
    private static class Bus<T> where T : struct, System.Enum
    {
        public static System.Action<T> Fired;
    }
}
