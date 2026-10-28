using MongoDB.Bson.Serialization.Attributes;

namespace ET
{
    /// <summary>
    /// 账号实体 - 对应原 MSSQL MudHxCenter.tUserInfo_Center
    /// Mongo collection: Account
    /// </summary>
    [ChildOf]
    public class Account : Entity, IAwake
    {
        [BsonElement]
        public string UserName { get; set; }       // szUserName (唯一索引)

        [BsonElement]
        public string PasswordHash { get; set; }   // szPassword (BCrypt)

        [BsonElement]
        public int OpenAccountNum { get; set; }    // iOpenAccountNum

        [BsonElement]
        public bool IsBanned { get; set; }

        [BsonElement]
        public string BanReason { get; set; }

        [BsonElement]
        public long LastLoginTime { get; set; }
    }
}
