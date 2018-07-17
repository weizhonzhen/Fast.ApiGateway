```csharp
//ConfigureServices 方法里 注入api网关
services.AddTransient<IResponse, ApiResponse>();

//Configure方法里 使用api网关 
app.UseMiddleware();

//mongodb里配url路由参数
public class UrlModel
    {
        /// <summary>
        /// key
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Composite(合并请求),Polling(轮循请求)
        /// </summary>
        public string Schema { get; set; }

        /// <summary>
        /// down param
        /// </summary>
        public List<DownParam> DownParam { get; set; }

        /// <summary>
        /// 是否缓存
        /// </summary>
        public bool IsCache { get; set; }

        /// <summary>
        /// 过期时间
        /// </summary>
        public DateTime TimeOut { get; set; }

        /// <summary>
        /// 是否匿名访问
        /// </summary>
        public bool IsAnonymous { get; set; }

        /// <summary>
        /// 是否获取token
        /// </summary>
        public bool IsGetToken { get; set; }
    }
	//下游参数
    public class DownParam
    {
        //down url
        public string Url { get; set; }

        /// <summary>
        /// method
        /// </summary>
        public string Method { get; set; }

        /// <summary>
        /// is body action
        /// </summary>
        public bool IsBody { get; set; }

        /// <summary>
        /// param
        /// </summary>
        public string Param { get; set; }
    }
}

//用户授权信息
internal class UserInfo
    {
        /// <summary>
        /// key
        /// </summary>
        public string AppKey { get; set; }

        /// <summary>
        /// 密钥
        /// </summary>
        public string AppSecret { get; set; }

        /// <summary>
        /// token
        /// </summary>
        public string AccessToken { get; set; }

        /// <summary>
        /// token过期时间 
        /// </summary>
        public DateTime AccessExpires { get; set; }
        
        /// <summary>
        /// token ip 
        /// </summary>
        public string Ip { get; set; }

        /// <summary>
        /// 一次访问次数
        /// </summary>
        public int DayCount { get; set; }
    }
```