#if UNITY_EDITOR || LEVEL_WORKSHOP
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Room list, new/duplicate/delete and prefab saving: every room is a prefab under "Room Map Prefabs".
public sealed partial class RoomWorkshopWindow
{
    private static bool PrefabMode => WorkshopPrefabs.Available;

    private sealed class RoomRow
    {
        internal string title, meta, key, path;
        internal RoomTag tag;
        internal bool draft, registered, broken;
        internal DateTime recent;
        internal WorkshopCatalog.Template template;
        internal Action open, duplicate, delete;
    }

    private int tagFilter = -1;
    private RoomRow pendingDelete;
    private bool savePanel, leavePrompt;
    private string saveName;
    private RoomTag saveTag;

    // ---------- Room list ----------

    private List<RoomRow> RoomRows()
    {
        var rows = new List<RoomRow>();
        var drafts = WorkshopDrafts.List(catalog);
        foreach (var draft in drafts)
        {
            var doc = draft.doc;
            var template = draft.error == null ? catalog.FindRoom(doc.template) : null;
            if (template != null && doc.id == doc.template) continue; // shown on its prefab row below
            var tag = template != null ? WorkshopPrefabs.TagOf(template) : RoomTag.Normal;
            rows.Add(new RoomRow
            {
                title = doc.title, key = doc.id, tag = tag, draft = true, broken = draft.error != null, recent = draft.time, template = template,
                meta = draft.error != null ? "열 수 없음 · " + draft.error : "제작 중 · " + WorkshopPrefabs.Label(tag) + " · 아직 저장 전",
                open = () => { if (draft.error != null) throw new System.IO.InvalidDataException(draft.error); Begin(doc, true); },
                duplicate = () => { if (draft.error != null) throw new System.IO.InvalidDataException(draft.error); Duplicate(doc); },
                delete = () => { WorkshopDrafts.Delete(doc.id); if (Doc?.id == doc.id) WorkshopTrial.Draft = null; }
            });
        }
        foreach (var template in catalog.rooms)
        {
            var tag = WorkshopPrefabs.TagOf(template);
            var draft = drafts.Find(d => d.error == null && d.doc.id == template.id);
            bool registered = WorkshopPrefabs.IsRegistered(catalog, template);
            long.TryParse(PlayerPrefs.GetString("Workshop.Opened." + template.id, "0"), out long opened);
            rows.Add(new RoomRow
            {
                title = template.title, key = template.id, tag = tag, template = template, registered = registered,
                path = WorkshopPrefabs.PathOf(template),
                recent = draft != null ? draft.time : opened > 0 ? new DateTime(opened, DateTimeKind.Utc) : DateTime.MinValue,
                meta = WorkshopPrefabs.Label(tag) + (draft != null ? " · 수정 중(임시 저장)" : "") + (registered ? " · 게임 등록됨" : " · 미등록"),
                open = () => { if (draft != null) Begin(draft.doc, true); else Begin(WorkshopRoom.FromTemplate(template), false); },
                duplicate = () => Duplicate(draft?.doc ?? WorkshopRoom.FromTemplate(template)),
                delete = () => { WorkshopPrefabs.Delete(template); if (Doc?.template == template.id) WorkshopTrial.Draft = null; }
            });
        }
        return rows.OrderByDescending(r => r.draft)
            .ThenByDescending(r => PlayerPrefs.GetInt("Workshop.Pinned." + r.key))
            .ThenByDescending(r => r.recent)
            .ThenBy(r => (int)r.tag).ThenBy(r => r.title, NaturalOrder.Instance).ToList();
    }

    private void DrawRoomList(string previousControl)
    {
        bool project = ProjectContentStore.Authoring;

        // Left: start something new.
        WorkshopUI.Frame(content, "New room", 8, 38, 223, 252);
        WorkshopUI.Label(content, "새 방 만들기", 20, 44, 200, 24, 15);
        WorkshopUI.Label(content, "태그를 고르면 빈 맵으로 시작합니다.", 20, 69, 204, 16);
        for (int i = 0; i < WorkshopPrefabs.Tags.Length; i++)
        {
            var tag = WorkshopPrefabs.Tags[i];
            Btn(WorkshopPrefabs.Label(tag), 20 + i % 2 * 102, 90 + i / 2 * 26, 98, 22, () => NewEmpty(tag)).name = "새 방 " + WorkshopPrefabs.Label(tag);
        }
        WorkshopUI.Label(content, "출구 간격", 20, 197, 60, 20);
        for (int i = 0; i < WorkshopPrefabs.Sizes.Length; i++)
        {
            int size = i;
            Btn(WorkshopPrefabs.Sizes[i].label, 82 + i * 46, 197, 44, 20, () => { newRoomSize = size; Draw(page); }, newRoomSize == i).name = "크기 " + WorkshopPrefabs.Sizes[i].label;
        }
        if (Doc != null)
            Btn("편집하던 방 계속 · " + Doc.title, 20, 223, 199, 24, () => OpenMap(false));
        WorkshopUI.Label(content, "저장하면 태그 폴더에 프리팹이 생깁니다.", 20, 252, 204, 16).color = WorkshopUI.Mint;
        WorkshopUI.Label(content, "RoomPrefabData 등록은 직접 해 주세요.", 20, 269, 204, 16);

        // Right: every room, newest work first.
        WorkshopUI.Frame(content, "Rooms", 239, 38, 393, 252);
        WorkshopUI.Label(content, "방 목록", 251, 43, 76, 24, 15);
        var search = Input("방 이름 검색", roomQuery, 330, 46, 150, 20);
        search.placeholder = WorkshopUI.Label(search.transform, "이름 또는 태그 검색", 5, 0, 140, 20);
        search.ForceLabelUpdate();
        void SearchRooms() { roomQuery = search.text.Trim(); listPage = 0; Draw(page); }
        search.onSubmit.AddListener(_ => Safe(SearchRooms));
        Btn("검색", 484, 46, 48, 20, SearchRooms);
        if (project) Btn("모두 저장", 536, 46, 90, 20, SaveAllDocuments);

        string[] chips = { "전체" };
        chips = chips.Concat(WorkshopPrefabs.Tags.Select(WorkshopPrefabs.ShortLabel)).ToArray();
        for (int i = 0; i < chips.Length; i++)
        {
            int filter = i - 1;
            Btn(chips[i], 251 + i * 41, 70, 39, 17, () => { tagFilter = filter; listPage = 0; Draw(page); }, tagFilter == filter).name = "필터 " + chips[i];
        }

        var rows = RoomRows();
        if (tagFilter >= 0) rows = rows.FindAll(r => (int)r.tag == tagFilter);
        if (roomQuery.Length > 0)
            rows = rows.FindAll(r => r.title.IndexOf(roomQuery, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     WorkshopPrefabs.Label(r.tag).IndexOf(roomQuery, StringComparison.OrdinalIgnoreCase) >= 0);

        const int perPage = 5;
        int pages = Mathf.Max(1, (rows.Count + perPage - 1) / perPage);
        listPage = Mathf.Clamp(listPage, 0, pages - 1);
        if (rows.Count == 0)
            WorkshopUI.Label(content, roomQuery.Length > 0 ? "'" + roomQuery + "' 에 맞는 방이 없습니다." : "이 태그의 방이 없습니다.", 253, 110, 360, 24, 12);
        for (int i = listPage * perPage; i < Math.Min(rows.Count, (listPage + 1) * perPage); i++)
        {
            var row = rows[i];
            int y = 92 + (i % perPage) * 33;
            var open = Btn(row.title, 251, y, 256, 30, row.open);
            open.name = "방 " + row.title;
            var title = open.GetComponentInChildren<TMP_Text>();
            title.rectTransform.anchoredPosition = new Vector2(33, -1);
            title.rectTransform.sizeDelta = new Vector2(218, 17);
            title.alignment = TextAlignmentOptions.MidlineLeft;
            PixelText.Configure(title, 12);
            var meta = WorkshopUI.Label(open.transform, row.meta, 33, 16, 218, 13);
            meta.color = row.broken ? new Color32(255, 140, 140, 255) : row.draft ? new Color32(252, 203, 103, 255) : WorkshopUI.Mint;
            RoomThumbnail(row.template, 254, y + 2, 26);
            Btn("복제", 511, y + 4, 38, 22, row.duplicate).name = "복제 " + row.title;
            Btn("삭제", 553, y + 4, 36, 22, () => { pendingDelete = row; Draw(page); }).name = "삭제 " + row.title;
            string pin = "Workshop.Pinned." + row.key;
            bool pinned = PlayerPrefs.GetInt(pin) != 0;
            Btn("고정", 593, y + 4, 33, 22, () => { PlayerPrefs.SetInt(pin, pinned ? 0 : 1); Draw(page); }, pinned).name = "고정 " + row.title;
        }
        Btn("이전", 251, 262, 50, 20, () => { listPage--; Draw(page); }).interactable = listPage > 0;
        WorkshopUI.Label(content, (listPage + 1) + " / " + pages + "  ·  " + rows.Count + "개", 307, 262, 118, 20);
        Btn("다음", 427, 262, 50, 20, () => { listPage++; Draw(page); }).interactable = listPage + 1 < pages;
        if (roomQuery.Length > 0 || tagFilter >= 0)
            Btn("검색 해제", 530, 262, 96, 20, () => { roomQuery = ""; tagFilter = -1; listPage = 0; Draw(page); });

        // Bottom: the other content editors.
        WorkshopUI.Frame(content, "Content tools", 8, 296, 624, 32);
        if (project)
        {
            string[] titles = { "플레이어 수치", "콘텐츠 수치", "대사 편집", "UI·보상 번역", "아트 원본" };
            Action[] actions = { () => Navigate(Page.Balance), OpenParameters, () => Navigate(Page.Dialogue), () => Navigate(Page.Translations), () => Navigate(Page.Art) };
            for (int i = 0; i < titles.Length; i++) Btn(titles[i], 14 + i * 123, 300, 118, 24, actions[i]);
        }
        else WorkshopUI.Label(content, "DATA 폴더를 찾지 못해 콘텐츠 편집을 쓸 수 없습니다.", 14, 300, 600, 24);

        if (pendingDelete != null) DrawDeleteConfirm();
        else if (previousControl is "방 이름 검색" or "검색") content.Find("검색")?.GetComponent<Button>()?.Select();
    }

    private void DrawDeleteConfirm()
    {
        var row = pendingDelete;
        Modal(150, 78, 340, 196);
        WorkshopUI.Label(content, "정말로 삭제할까요?", 166, 88, 300, 24, 15);
        WorkshopUI.Label(content, "이름  ·  " + row.title, 166, 116, 310, 18, 12);
        WorkshopUI.Label(content, "태그  ·  " + WorkshopPrefabs.Label(row.tag) + (row.draft ? "  (저장 전 임시 작업)" : ""), 166, 136, 310, 18, 12);
        WorkshopUI.Label(content, row.draft ? "임시 저장을 지웁니다. 되돌릴 수 없습니다." : "파일  ·  " + row.path, 166, 158, 310, 16);
        if (!row.draft)
            WorkshopUI.Label(content, "프리팹은 휴지통으로 이동해 복구할 수 있습니다.", 166, 176, 310, 16);
        if (row.registered)
            WorkshopUI.Label(content, "주의 · RoomPrefabData에 등록된 방입니다. 등록도 함께 정리하세요.", 166, 196, 310, 16).color = new Color32(255, 140, 140, 255);
        Btn("삭제", 166, 240, 150, 24, () =>
        {
            pendingDelete = null;
            row.delete();
            thumbnails.Remove(row.key);
            Draw(page);
            Say("삭제했습니다 · " + row.title);
        }).name = "삭제 확인";
        Btn("취소", 324, 240, 150, 24, () => { pendingDelete = null; Draw(page); }, true).Select();
    }

    private void Modal(int x, int y, int w, int h)
    {
        // A full-screen blocker keeps clicks from reaching the page underneath.
        WorkshopUI.Box(content, "Modal blocker", 0, 30, 640, 330, new Color(0, 0, 0, .55f));
        WorkshopUI.Frame(content, "Modal", x, y, w, h);
    }

    // ---------- New and duplicate ----------

    private void NewEmpty(RoomTag tag)
    {
        var template = WorkshopPrefabs.BaseFor(catalog, tag) ?? throw new System.IO.InvalidDataException(WorkshopPrefabs.Label(tag) + " 태그의 기준 방이 없습니다.");
        var doc = WorkshopPrefabs.Empty(template, WorkshopDrafts.NextNewName(WorkshopDrafts.List(catalog)), newRoomSize);
        Begin(doc, true);
        Say("빈 " + WorkshopPrefabs.Label(tag) + " 방 · 바닥부터 그려 보세요. 저장할 때 이름과 태그를 정합니다.");
    }

    private void Duplicate(WorkshopRoom source)
    {
        var copy = JsonUtility.FromJson<WorkshopRoom>(source.Serialize());
        copy.id = Guid.NewGuid().ToString("N");
        copy.title = source.title + " 복사본";
        Begin(copy, true);
        Say("복제본을 열었습니다 · 저장할 때 이름과 태그를 정합니다.");
    }

    // ---------- Saving ----------

    private bool IsSavedPrefab => Doc != null && Doc.id == Doc.template;

    private void OpenSavePanel()
    {
        FinishMapEdits();
        var template = catalog.FindRoom(Doc.template);
        saveTag = WorkshopPrefabs.TagOf(template);
        saveName = IsSavedPrefab ? template.title : WorkshopPrefabs.SuggestName(saveTag, catalog);
        savePanel = true; leavePrompt = false; drawer = false; encounterOpen = false;
        Draw(page);
    }

    private void QuickSave()
    {
        if (!IsSavedPrefab) { OpenSavePanel(); return; }
        var template = catalog.FindRoom(Doc.template);
        SavePrefab(WorkshopPrefabs.TagOf(template), template.title);
    }

    private void SavePrefab(RoomTag tag, string name)
    {
        FinishMapEdits();
        var view = (preview.Center, preview.Zoom);
        string oldId = Doc.id;
        string path = WorkshopPrefabs.Save(Doc, catalog, tag, name);
        WorkshopDrafts.Delete(oldId);
        thumbnails.Remove(oldId);
        string guid = null;
#if UNITY_EDITOR
        guid = UnityEditor.AssetDatabase.AssetPathToGUID(path);
#endif
        var saved = catalog.FindRoom(guid) ?? throw new System.IO.IOException("저장한 방이 카탈로그에 없습니다 · " + path);
        thumbnails.Remove(saved.id);
        savePanel = false;
        Begin(WorkshopRoom.FromTemplate(saved), false);
        preview.Center = view.Center; preview.Zoom = view.Zoom;
        if (view.Zoom == 0) preview.Overview(); else preview.SetView();
        WorkshopPrefabs.Reveal(path);
        Draw(page);
        Say("저장됨 · " + path + (WorkshopPrefabs.IsRegistered(catalog, saved) ? "" : " · RoomPrefabData 미등록"));
    }

    private void DrawSavePanel()
    {
        Modal(100, 52, 440, 252);
        WorkshopUI.Label(content, "방 저장", 116, 60, 200, 24, 15);
        WorkshopUI.Label(content, "이름", 116, 92, 40, 20);
        var field = Input("저장 이름", saveName, 160, 92, 230, 20);
        Btn("추천 이름", 396, 92, 128, 20, () => { saveName = WorkshopPrefabs.SuggestName(saveTag, catalog); Draw(page); });
        WorkshopUI.Label(content, "태그", 116, 120, 40, 20);
        for (int i = 0; i < WorkshopPrefabs.Tags.Length; i++)
        {
            var tag = WorkshopPrefabs.Tags[i];
            Btn(WorkshopPrefabs.Label(tag), 160 + i % 4 * 92, 120 + i / 4 * 24, 88, 20, () =>
            {
                bool suggested = saveName == WorkshopPrefabs.SuggestName(saveTag, catalog);
                saveTag = tag;
                if (suggested || !IsSavedPrefab) saveName = WorkshopPrefabs.SuggestName(saveTag, catalog);
                Draw(page);
            }, saveTag == tag);
        }
        var where = WorkshopUI.Label(content, "", 116, 172, 410, 16);
        var state = WorkshopUI.Label(content, "", 116, 190, 410, 16);
        string warning = RoomWarnings();
        var note = WorkshopUI.Label(content, warning != null ? "확인 · " + warning : "RoomPrefabData 등록은 직접 해 주세요.", 116, 208, 410, 16);
        if (warning != null) note.color = new Color32(252, 203, 103, 255);
        Button confirm = null;
        void Refresh(string typed)
        {
            saveName = typed;
            string problem = WorkshopPrefabs.CheckName(typed);
            string target = WorkshopPrefabs.PathFor(saveTag, (typed ?? "").Trim());
            string source = WorkshopPrefabs.PathOf(catalog.FindRoom(Doc.template));
            where.text = "저장 위치  ·  " + target;
            bool exists = System.IO.File.Exists(target);
            bool overwrite = target == source && IsSavedPrefab;
            state.text = problem ?? (overwrite ? "기존 프리팹을 덮어씁니다." : exists ? "같은 이름의 방이 이미 있습니다. 다른 이름을 쓰세요." : "새 프리팹으로 저장합니다.");
            state.color = problem != null || exists && !overwrite ? new Color32(255, 140, 140, 255) : WorkshopUI.Mint;
            if (confirm != null)
            {
                confirm.interactable = problem == null && (!exists || overwrite);
                confirm.GetComponentInChildren<TMP_Text>().text = overwrite ? "덮어쓰기 저장" : "새로 저장";
            }
        }
        field.onValueChanged.AddListener(Refresh);
        confirm = Btn("저장", 116, 236, 200, 26, () => SavePrefab(saveTag, field.text), true);
        confirm.name = "저장 확인";
        Btn("취소", 324, 236, 200, 26, () => { savePanel = false; Draw(page); });
        Refresh(saveName);
    }

    // ---------- Leaving the editor ----------

    private void RequestLeave()
    {
        FinishMapEdits();
        if (dirty) { leavePrompt = true; savePanel = false; Draw(page); return; }
        Navigate(Page.Desk);
    }

    private void DrawLeavePrompt()
    {
        Modal(150, 90, 340, 160);
        WorkshopUI.Label(content, "저장하지 않은 변경이 있습니다", 166, 100, 300, 24, 15);
        WorkshopUI.Label(content, "작업은 임시 저장되어 목록의 '제작 중'에 남습니다.", 166, 130, 310, 18);
        WorkshopUI.Label(content, "언제든 다시 열어 이어서 할 수 있습니다.", 166, 150, 310, 18);
        Btn("목록으로", 166, 210, 96, 24, () => { leavePrompt = false; Navigate(Page.Desk); }, true).name = "목록으로";
        Btn("저장하기", 270, 210, 96, 24, () => { leavePrompt = false; OpenSavePanel(); });
        Btn("계속 편집", 374, 210, 100, 24, () => { leavePrompt = false; Draw(page); });
    }
}
#endif
