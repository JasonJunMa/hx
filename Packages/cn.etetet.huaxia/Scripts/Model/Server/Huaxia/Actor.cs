using MongoDB.Bson.Serialization.Attributes;

namespace ET
{
    /// <summary>
    /// 角色实体 - 对应原 MSSQL MudHx.tActorInfo
    /// Mongo collection: Actor
    /// </summary>
    [ChildOf]
    public class Actor : Entity, IAwake
    {
        [BsonElement]
        public long UserId { get; set; }           // iUserID (关联 Account.Id)

        [BsonElement]
        public string ActorName { get; set; }      // szActorName

        [BsonElement]
        public int Class { get; set; }             // iClass (职业: 战士/法师/暗巫/幻师)

        [BsonElement]
        public int Level { get; set; }             // iLevel

        [BsonElement]
        public int Sex { get; set; }               // iSex

        // ---- 位置 ----
        [BsonElement]
        public int MapId { get; set; }             // iMapID

        [BsonElement]
        public int LocationX { get; set; }         // iLocationX

        [BsonElement]
        public int LocationY { get; set; }         // iLocationY

        // ---- 原 binBaseBuffer (256字节 STLport blob) ----
        [BsonElement]
        public byte[] BaseBuffer { get; set; }

        // ---- 基础属性 ----
        [BsonElement]
        public int Strength { get; set; }          // iStrength

        [BsonElement]
        public int Agility { get; set; }

        [BsonElement]
        public int Intelligence { get; set; }

        [BsonElement]
        public int MaxHp { get; set; }

        [BsonElement]
        public int CurHp { get; set; }

        [BsonElement]
        public int MaxMp { get; set; }

        [BsonElement]
        public int CurMp { get; set; }

        [BsonElement]
        public long Exp { get; set; }

        // ---- 密码保护 ----
        [BsonElement]
        public string StoragePass { get; set; }    // szStoragePass
    }
}
