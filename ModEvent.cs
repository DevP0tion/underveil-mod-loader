using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UnderveilModLoader
{
    /// <summary>
    /// 이 특성을 붙인 <see cref="ModEvent"/> 클래스는 모드를 불러올 때 자동으로 생성되어 게임의 이벤트 풀에 추가됩니다.
    /// 추상 클래스가 아니어야 하고, 인자 없는 생성자가 있어야 합니다.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class RegisterAttribute : Attribute
    {
        /// <summary>
        /// 이 이벤트의 문구가 들어 있는 언어 파일 이름입니다. <c>null</c>이면 클래스 이름을 씁니다.
        /// </summary>
        public string TextFile { get; }

        /// <param name="textFile">언어 파일 이름입니다. 확장자는 빼고, 생략하면 클래스 이름을 씁니다.</param>
        public RegisterAttribute(string textFile = null)
        {
            TextFile = textFile;
        }
    }

    /// <summary>
    /// 모드 이벤트의 기반 클래스입니다. 상속한 뒤 <see cref="RegisterAttribute"/>를 붙이면 게임의 이벤트 풀에 추가됩니다.
    /// </summary>
    public abstract class ModEvent
    {
        internal static readonly List<ModEvent> Registered = new List<ModEvent>();

        internal ModEntry Owner;

        internal string TextFile;

        /// <summary>
        /// 이벤트 id입니다. 기본값은 모드 id와 클래스 전체 이름의 해시이며, 둘 중 하나가 바뀌면 id도 바뀝니다.
        /// 게임 이벤트나 다른 모드 이벤트와 겹치면 이 이벤트는 추가되지 않습니다.
        /// </summary>
        public virtual int EventId => StableId($"{Owner.Id}.{GetType().FullName}");

        /// <summary>
        /// 이벤트 제목의 언어 키입니다. 기본값은 <c>Text("name")</c>입니다.
        /// </summary>
        protected virtual string NameKey => Text("name");

        /// <summary>
        /// 이벤트 설명의 언어 키입니다. 기본값은 <c>Text("description")</c>입니다.
        /// </summary>
        protected virtual string DescriptionKey => Text("description");

        /// <summary>
        /// 한 번 나온 뒤에도 다시 나올 수 있는지 정합니다. 기본값은 <c>false</c>입니다.
        /// </summary>
        protected virtual bool Repeatable => false;

        /// <summary>
        /// 이벤트가 나올 챕터입니다. <c>0</c>이면 모든 챕터에서 나오고, 그 외에는 게임의 <c>chapter + 1</c>이 이 값과 같을 때만 나옵니다. 기본값은 <c>0</c>입니다.
        /// </summary>
        protected virtual int MinChapter => 0;

        /// <summary>
        /// 이 이벤트가 나올 수 있는지 판단합니다. 게임이 이벤트를 뽑을 때마다 호출하며, 기본값은 항상 <c>true</c>입니다.
        /// 예외가 나면 오류를 로그에 남기고 <c>false</c>로 처리합니다.
        /// 나올 수 있는 이벤트가 하나도 없으면 게임이 조건을 무시하고 전체 이벤트에서 뽑으므로 이때는 나올 수 있습니다.
        /// </summary>
        /// <param name="player">현재 플레이어 데이터입니다.</param>
        /// <returns>이벤트가 나올 수 있으면 <c>true</c>입니다.</returns>
        protected virtual bool Requirement(PlayerData player) => true;

        /// <summary>
        /// 이벤트의 선택지 목록을 만듭니다. 게임의 이벤트 관리자가 초기화될 때 호출됩니다.
        /// </summary>
        /// <returns>이벤트에 표시할 선택지 목록입니다.</returns>
        protected abstract List<EventChoice> CreateChoices();

        /// <summary>
        /// 이벤트 이미지를 고릅니다. 기본 동작은 이미 등록된 이벤트 중 이미지가 있는 첫 이벤트의 이미지를 빌려 씁니다.
        /// </summary>
        /// <param name="events">지금까지 이벤트 풀에 등록된 이벤트 목록입니다.</param>
        /// <returns>사용할 이미지입니다. 없으면 <c>null</c>입니다.</returns>
        protected virtual Sprite GetArtwork(IReadOnlyList<EventData> events)
        {
            return events.Select(e => e.artwork).FirstOrDefault(a => a != null);
        }

        /// <summary>
        /// 이 이벤트의 언어 파일에 있는 키를 <c>{모드 id}.{언어 파일 이름}.{key}</c> 형식의 전체 키로 바꿉니다.
        /// 등록 후에 값이 정해지므로 생성자에서는 호출할 수 없습니다.
        /// </summary>
        /// <param name="key">언어 파일 안의 키입니다.</param>
        /// <returns>게임 데이터에 넣을 전체 키입니다.</returns>
        protected string Text(string key) => $"{Owner.Id}.{TextFile}.{key}";

        /// <summary>
        /// 문자열에서 실행마다 같은 id를 만듭니다. 게임이 id를 저장하므로 값이 변하면 안 되고, 그래서 <c>string.GetHashCode</c> 대신 FNV-1a를 씁니다.
        /// </summary>
        /// <param name="text">해시할 문자열입니다.</param>
        /// <returns>게임 이벤트 id보다 높은 범위의 양수 id입니다.</returns>
        private static int StableId(string text)
        {
            uint hash = 2166136261;
            foreach (char c in text)
            {
                hash = (hash ^ c) * 16777619;
            }
            return (int)(hash % 2000000000u) + 100000000;
        }

        /// <summary>
        /// 모드의 타입 중 <see cref="RegisterAttribute"/>가 붙은 이벤트를 생성해 등록합니다.
        /// 특성을 잘못 쓰면 예외를 던져 모드 창에서 원인을 볼 수 있게 합니다.
        /// </summary>
        /// <param name="entry">이벤트를 소유한 모드입니다.</param>
        /// <param name="types">모드 어셈블리의 타입 목록입니다.</param>
        internal static void RegisterAll(ModEntry entry, Type[] types)
        {
            foreach (Type type in types)
            {
                RegisterAttribute attribute = type.GetCustomAttribute<RegisterAttribute>(false);
                if (attribute == null)
                {
                    continue;
                }
                if (type.IsAbstract || !typeof(ModEvent).IsAssignableFrom(type))
                {
                    throw new InvalidOperationException($"[Register] needs a non-abstract ModEvent class: {type.Name}");
                }
                string textFile = attribute.TextFile ?? type.Name;
                if (string.IsNullOrWhiteSpace(textFile))
                {
                    throw new InvalidOperationException($"[Register] has an empty language file name: {type.Name}");
                }
                ModEvent modEvent = (ModEvent)Activator.CreateInstance(type, true);
                modEvent.Owner = entry;
                modEvent.TextFile = textFile;
                Registered.Add(modEvent);
            }
        }

        /// <summary>
        /// 불러오기에 실패한 모드의 이벤트를 등록 목록에서 제거합니다.
        /// </summary>
        /// <param name="entry">실패한 모드입니다.</param>
        internal static void Unload(ModEntry entry) => Registered.RemoveAll(e => e.Owner == entry);

        /// <summary>
        /// 이 이벤트의 값으로 게임의 <see cref="EventData"/>를 만듭니다.
        /// </summary>
        /// <param name="events">지금까지 이벤트 풀에 등록된 이벤트 목록입니다.</param>
        /// <returns>이벤트 풀에 추가할 데이터입니다.</returns>
        internal EventData Build(IReadOnlyList<EventData> events)
        {
            EventData data = ScriptableObject.CreateInstance<EventData>();
            data.eventId = EventId;
            data.eventName = NameKey;
            data.description = DescriptionKey;
            data.artwork = GetArtwork(events);
            data.oneShot = !Repeatable;
            data.minChapter = MinChapter;
            data.choices = CreateChoices();
            data.requiredConditionIds = new List<string> { ConditionId };
            return data;
        }

        internal string ConditionId => $"mod.event.{EventId}";

        /// <summary>
        /// <see cref="Requirement"/>를 호출합니다. 모드의 예외가 게임의 이벤트 추첨을 멈추지 않게 막습니다.
        /// </summary>
        /// <param name="player">현재 플레이어 데이터입니다.</param>
        /// <returns>이벤트가 나올 수 있으면 <c>true</c>입니다.</returns>
        internal bool CheckRequirement(PlayerData player)
        {
            try
            {
                return Requirement(player);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[{Owner.Id}] Requirement of {GetType().Name} failed: {e.GetType().Name}: {e.Message}");
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(EventManager), "Awake")]
    internal static class EventManagerAwakePatch
    {
        private static void Postfix(List<EventData> ___allEvents, Dictionary<string, Func<PlayerData, bool>> ___conditionHandlers)
        {
            foreach (ModEvent modEvent in ModEvent.Registered)
            {
                // Mods are third-party code, and an exception here would stop the game's event manager.
                try
                {
                    if (___allEvents.Any(e => e.eventId == modEvent.EventId))
                    {
                        Plugin.Log.LogError($"[{modEvent.Owner.Id}] Event id already in use: {modEvent.EventId}");
                        continue;
                    }
                    ___allEvents.Add(modEvent.Build(___allEvents));
                    ___conditionHandlers[modEvent.ConditionId] = modEvent.CheckRequirement;
                    Plugin.Log.LogInfo($"[{modEvent.Owner.Id}] Added event {modEvent.EventId}");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"[{modEvent.Owner.Id}] Cannot add event {modEvent.GetType().Name}: {e.GetType().Name}: {e.Message}");
                }
            }
        }
    }
}
