// ESC 입력 소유권 (SystemMenu.md 4장, ControlsHelp.md 6장)
//   ESC 한 번은 가장 위의 UI 한 곳만 처리한다. 드롭다운·컬러피커는 자기 키 이벤트로 ESC 를 처리하면서 이 프레임을 표시한다.
//   AppController 의 전역 ESC 처리는 이미 처리된 프레임이면 아무것도 하지 않는다(메뉴를 같이 열지 않는다).
//   UI 이벤트와 전역 Update 의 실행 순서가 프레임마다 달라도 같은 ESC 가 두 번 처리되지 않게 다음 프레임까지 본다.
using UnityEngine;

public static class UIEscape
{
    static int consumedFrame = -10;

    public static void Consume() => consumedFrame = Time.frameCount;

    public static bool ConsumedRecently => Time.frameCount - consumedFrame <= 1;
}
