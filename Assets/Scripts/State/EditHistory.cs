// 꾸미기 실행 취소·다시 실행 이력 (UndoRedo.md, 2026-11-03 확정)
//   대상: 외형·의상·염색·배경(꾸미기 값). 카메라(회전·팬·거리·화각)와 UI·환경 설정은 제외한다.
//   스냅숏은 장면 상태 복사본이지만 카메라를 비워 둔다. 복원할 때는 꾸미기 값만 쓰고 현재 카메라를 유지한다.
//   이력은 현재 실행에만 있다. JSON·자동 저장에 넣지 않는다. 가져오기·초기화 성공 시 Clear 한다.
using System.Collections.Generic;
using UnityEngine;

public sealed class EditHistory
{
    readonly List<EditState> undo = new List<EditState>();
    readonly List<EditState> redo = new List<EditState>();

    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;

    /// <summary>꾸미기 값만 남긴 복사본(카메라·앱 버전 제외).</summary>
    public static EditState Snapshot(EditState s)
    {
        var c = s.Clone();
        c.camera = new CameraState();
        c.appVersion = null;
        return c;
    }

    /// <summary>두 상태의 꾸미기 값이 같은지(카메라·앱 버전은 비교하지 않는다).</summary>
    public static bool SameLook(EditState a, EditState b) =>
        JsonUtility.ToJson(Snapshot(a)) == JsonUtility.ToJson(Snapshot(b));

    /// <summary>배경을 뺀 꾸미기 값(외형·의상·염색)이 같은지. 같으면 캐릭터를 다시 조립하지 않아도 된다.</summary>
    public static bool SameCharacter(EditState a, EditState b)
    {
        var x = Snapshot(a); x.background = null;
        var y = Snapshot(b); y.background = null;
        return JsonUtility.ToJson(x) == JsonUtility.ToJson(y);
    }

    /// <summary>새 꾸미기 편집이 확정됐다. 편집 전 상태를 넣고 다시 실행 분기를 비운다. 꾸미기 값이 같으면 기록하지 않는다.</summary>
    public bool Record(EditState before, EditState after)
    {
        if (before == null || after == null || SameLook(before, after)) return false;
        undo.Add(Snapshot(before));
        redo.Clear();
        return true;
    }

    public EditState PeekUndo() => undo.Count > 0 ? undo[undo.Count - 1] : null;
    public EditState PeekRedo() => redo.Count > 0 ? redo[redo.Count - 1] : null;

    /// <summary>실행 취소가 성공했다. 꺼낸 상태를 버리고 그 직전 상태(current)를 다시 실행 쪽에 넣는다.</summary>
    public void CommitUndo(EditState current)
    {
        undo.RemoveAt(undo.Count - 1);
        redo.Add(Snapshot(current));
    }

    /// <summary>다시 실행이 성공했다.</summary>
    public void CommitRedo(EditState current)
    {
        redo.RemoveAt(redo.Count - 1);
        undo.Add(Snapshot(current));
    }

    public void Clear()
    {
        undo.Clear();
        redo.Clear();
    }
}
