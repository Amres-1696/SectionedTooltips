#if DEVDUMP
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SectionedTooltips
{
    /// <summary>
    /// dev 构建专用：模组数据目录里有 shots.flag 时，把屏幕上显示出来的每张估价单按真实样子截成 PNG
    /// （shot_物品名.png，收起和展开各一张），给游戏外评设计用。每种每局只截一次，重开游戏再截会覆盖。
    /// 单子出现半秒后在帧末读屏幕像素，这时淡入和位置都已经稳了。
    /// </summary>
    internal sealed class Shots : MonoBehaviour
    {
        private const string ModId = "amres.sectioned_tooltips";
        private const string Flag = "shots.flag";
        private const float Settle = 0.5f;
        private const int Margin = 6;

        private static Shots host;
        private static bool on, busy;
        private static float checkedAt = -10f;
        private static readonly HashSet<string> done = new HashSet<string>();
        private static string pending;
        private static RectTransform pendingCard;
        private static float since;

        /// 估价单显示着的每一帧调用。
        internal static void Seen(RectTransform card, string name)
        {
            if (card == null || busy || !Enabled() || done.Contains(name)) return;
            if (pending != name || !ReferenceEquals(pendingCard, card))
            {
                pending = name;
                pendingCard = card;
                since = Time.unscaledTime;
                return;
            }
            if (Time.unscaledTime - since < Settle) return;
            if (host == null)
            {
                GameObject go = new GameObject("SectionedTooltips.Shots");
                DontDestroyOnLoad(go);
                host = go.AddComponent<Shots>();
            }
            busy = true;
            done.Add(name);
            host.StartCoroutine(host.Grab(card, name));
        }

        private static bool Enabled()
        {
            if (Time.unscaledTime - checkedAt < 2f) return on;
            checkedAt = Time.unscaledTime;
            try { on = ModHelper.ModDataFileExists(ModId, Flag); }
            catch { on = false; }
            return on;
        }

        private IEnumerator Grab(RectTransform card, string name)
        {
            yield return new WaitForEndOfFrame();
            try
            {
                if (card != null && card.gameObject.activeInHierarchy) Save(card, name);
            }
            catch (Exception e) { SectionedTooltipsMod.Log("截图失败：" + name + "：" + e.Message); }
            busy = false;
        }

        private static void Save(RectTransform card, string name)
        {
            Vector3[] corners = new Vector3[4];
            card.GetWorldCorners(corners);
            Canvas canvas = card.GetComponentInParent<Canvas>();
            if (canvas != null) canvas = canvas.rootCanvas;
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, b.x)) - Margin, 0, Screen.width);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, b.y)) - Margin, 0, Screen.height);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, b.x)) + Margin, 0, Screen.width);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, b.y)) + Margin, 0, Screen.height);
            if (x1 - x0 < 8 || y1 - y0 < 8) return;
            Texture2D tex = new Texture2D(x1 - x0, y1 - y0, TextureFormat.RGB24, false);
            try
            {
                tex.ReadPixels(new Rect(x0, y0, x1 - x0, y1 - y0), 0, 0);
                tex.Apply();
                byte[] png = ImageConversion.EncodeToPNG(tex);
                string file = "shot_" + FileName(name) + ".png";
                ModHelper.WriteModDataFileBytes(ModId, file, png);
                SectionedTooltipsMod.Log(string.Format("截图：{0}（{1}x{2}）", file, x1 - x0, y1 - y0));
            }
            finally { Destroy(tex); }
        }

        private static string FileName(string name)
        {
            char[] bad = System.IO.Path.GetInvalidFileNameChars();
            char[] s = name.ToCharArray();
            for (int i = 0; i < s.Length; i++) if (Array.IndexOf(bad, s[i]) >= 0 || s[i] == ' ') s[i] = '_';
            return new string(s);
        }
    }
}
#endif
