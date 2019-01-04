﻿using Microsoft.AspNetCore.Http;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using FastUntility.Core.Base;
using FastData.Core;
using FastData.Core.Context;
using FastApiGatewayDb.DataModel;
using FastApiGatewayDb.Model;

namespace FastApiGatewayDb
{
    public class FastApiGatewayDb : IFastApiGatewayDb
    {
        //接口数据库
        public static string DbApi = "ApiGateway";
        public void Content(HttpContext context)
        {
            using (var db = new DataContext(DbApi))
            {
                context.Response.ContentType = "application/Json";
                var key = context.Request.Path.Value.ToStr().Replace("/", "").ToUpper();
                                
            if (FastRead.Query<ApiGatewayUrl>(a => a.Key.ToUpper() == key.ToUpper()).ToCount(db) <= 0 || string.IsNullOrEmpty(key))
                {
                    var dic = new Dictionary<string, object>();
                    dic.Add("success", false);
                    dic.Add("result", string.Format("请求地址{0}无效", key));
                    context.Response.StatusCode = 404;
                    context.Response.WriteAsync(JsonConvert.SerializeObject(dic).ToString(), Encoding.UTF8);
                }
                else
                {                    
                    var item = FastRead.Query<ApiGatewayUrl>(a => a.Key.ToUpper() == key.ToUpper()).ToItem<ApiGatewayUrl>() ?? new ApiGatewayUrl();
                    var downParam = FastRead.Query<ApiGatewayDownParam>(a => a.Key.ToUpper() == key.ToUpper()).ToList<ApiGatewayDownParam>();

                    //获取token
                    if (item.IsGetToken==1)
                        Token(context,db);
                    else
                    {
                        //是否匿名访问
                        if (item.IsAnonymous == 0)
                            if (!CheckToken(item, context,db))
                                return;

                        //结果是否缓存
                        if (item.IsCache==1)
                        {                            
                            var resultInfo = FastRead.Query<ApiGatewayCache>(a => a.Key.ToUpper() == key).ToItem<ApiGatewayCache>(db);
                            if (DateTime.Compare(resultInfo.TimeOut, DateTime.Now) > 0)
                            {
                                context.Response.StatusCode = 200;
                                context.Response.WriteAsync(resultInfo.result, Encoding.UTF8);
                            }
                            else
                            {
                                if (item.Schema.ToStr().ToLower() == "polling") //polling 轮循请求
                                    Polling(item, context,db,downParam);
                                else if (item.Schema.ToStr().ToLower() == "composite") //composite 合并请求
                                    Composite(item, context,db, downParam);
                                else
                                    Normal(item, context, db, downParam);
                            }
                        }
                        else
                        {
                            if (item.Schema.ToStr().ToLower() == "polling") //polling 轮循请求
                                Polling(item, context,db, downParam);
                            else if (item.Schema.ToStr().ToLower() == "composite") //composite 合并请求
                                Composite(item, context,db, downParam);
                            else
                                Normal(item, context,db, downParam);
                        }
                    }
                }
            }
        }

        #region 处理请求
        /// <summary>
        /// 处理请求
        /// </summary>
        /// <param name="downparam">下游</param>
        /// <param name="param">请求参数</param>
        /// <param name="content">请求参数body</param>
        /// <returns></returns>
        private ReturnModel GetReuslt(ApiGatewayDownParam downparam, string param, string content, string key,int isLog,DataContext db)
        {
            var info = FastRead.Query<ApiGatewayWait>(a => a.Key.ToLower() == key.ToLower() && a.Url.ToLower() == downparam.Url.ToLower()).ToItem<ApiGatewayWait>(db) ?? new ApiGatewayWait();
            if (info.Key.ToStr().ToLower() == key.ToLower() && DateTime.Compare(info.NextAction, DateTime.Now) > 0)
            {
                //return time out
                var dic = new Dictionary<string, object>();
                dic.Add("success", false);
                dic.Add("result", "等待恢复");
                return new ReturnModel { msg = BaseJson.ModelToJson(dic), status = 408 };
            }
            else
            {
                var result = new ReturnModel();
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                if (downparam.Protocol.ToLower() == "soap")
                {  
                    //soap
                    result = BaseUrl.SoapUrl(downparam.Url, downparam.SoapParamName, param, downparam.SoapMethod);
                }
                else if (downparam.Protocol.ToLower() == "http")
                {
                    //http
                    if (downparam.Method.ToStr().ToLower() == "post")
                    {
                        if (downparam.IsBody==1)
                            result = BaseUrl.PostContent(downparam.Url, content,key);
                        else
                            result = BaseUrl.PostUrl(downparam.Url, param,key);
                    }
                    else if (downparam.Method.ToStr().ToLower() == "get")
                        result = BaseUrl.GetUrl(downparam.Url, param,key);
                }
                //else if (downparam.Protocol.ToLower() == "mq")
                //    //mq
                //    result = BaseUrl.RabbitUrl(downparam.QueueName, param);
                else
                    result.status = 408;

                if (result.status == 408)
                    //time out
                    Task.Factory.StartNew(() =>
                    {
                        var wait = new ApiGatewayWait();
                        wait.Key = key;
                        wait.Url = downparam.Url;
                        wait.WaitHour = downparam.WaitHour.ToInt(0);
                        wait.ErrorMsg = result.msg;
                        wait.NextAction = DateTime.Now.AddHours(wait.WaitHour.ToStr().ToDouble(0)).AddHours(8);

                        FastWrite.Add(wait, db);
                    });
                else if (info.Key.ToStr().ToLower() == key.ToLower())
                    //log
                    Task.Factory.StartNew(() =>
                    {
                        FastWrite.Delete<ApiGatewayWait>(a => a.Key.ToLower() == key.ToLower(), db);
                    });

                stopwatch.Stop();

                if (isLog == 1)
                    Task.Factory.StartNew(() =>
                    {
                        var logInfo = new ApiGatewayLog();
                        logInfo.Key = key;
                        logInfo.ActionTime = DateTime.Now;
                        logInfo.Url = downparam.Url;
                        logInfo.Protocol = downparam.Protocol;
                        logInfo.Success = result.status == 200 ? 1 : 0;
                        logInfo.Milliseconds = stopwatch.Elapsed.TotalMilliseconds.ToStr();
                        FastWrite.Add(logInfo, db);
                    });

                return result;
            }
        }
        #endregion

        #region 验证token
        /// <summary>
        /// 验证token
        /// </summary>
        /// <param name="item"></param>
        /// <param name="context"></param>
        private bool CheckToken(ApiGatewayUrl item, HttpContext context,DataContext db)
        {
            var dic = new Dictionary<string, object>();
            var token = "";
            if (context.Request.Method.ToLower() == "get")
                token = context.Request.Query["token"].ToStr().ToUpper();
            else if (context.Request.Method.ToLower() == "post")
                token = context.Request.Form["token"].ToStr().ToUpper();

            if (FastRead.Query<ApiGatewayUser>(a => a.AccessToken.ToUpper() == token).ToCount(db) == 0)
            {
                context.Response.StatusCode = 200;
                dic.Add("success", false);
                dic.Add("result", "token不存在");
                context.Response.WriteAsync(BaseJson.ModelToJson(dic), Encoding.UTF8);
                return false;
            }
            else
            {
                var tokenInfo = FastRead.Query<ApiGatewayUser>(a => a.AccessToken.ToUpper() == token).ToItem<ApiGatewayUser>(db);
                if (DateTime.Compare(tokenInfo.AccessExpires, DateTime.Now) < 0)
                {
                    context.Response.StatusCode = 200;
                    dic.Add("success", false);
                    dic.Add("result", "token过期");
                    context.Response.WriteAsync(BaseJson.ModelToJson(dic), Encoding.UTF8);
                    return false;
                }

                if (tokenInfo.Ip != GetClientIp(context))
                {
                    context.Response.StatusCode = 200;
                    dic.Add("success", false);
                    dic.Add("result", "token授权ip地址异常");
                    context.Response.WriteAsync(BaseJson.ModelToJson(dic), Encoding.UTF8);
                    return false;
                }

                if (tokenInfo.Power.IndexOf(',') > 0)
                {
                    foreach(var temp in tokenInfo.Power.Split(','))
                    {
                        if (temp.ToLower() == item.Key.ToLower())
                            return true;
                    }

                    context.Response.StatusCode = 200;
                    dic.Add("success", false);
                    dic.Add("result", string.Format("{0}没有权限访问", item.Key));
                    context.Response.WriteAsync(BaseJson.ModelToJson(dic), Encoding.UTF8);
                    return false;
                }
                else
                {
                    context.Response.StatusCode = 200;
                    dic.Add("success", false);
                    dic.Add("result", string.Format("{0}没有权限访问", item.Key));
                    context.Response.WriteAsync(BaseJson.ModelToJson(dic), Encoding.UTF8);
                    return false;
                }
            }
        }
        #endregion

        #region 普通请求
        /// <summary>
        /// 普通请求
        /// </summary>
        private void Normal(ApiGatewayUrl item, HttpContext context,DataContext db,List<ApiGatewayDownParam> list)
        {
            var content = new StreamReader(context.Request.Body).ReadToEnd();
            var param = context.Request.QueryString.Value;
            
            var downparam = list.FirstOrDefault() ?? new ApiGatewayDownParam();
            var info = GetReuslt(downparam, param, content, item.Key, item.IsLog, db);

            //缓存结果
            if (item.IsCache == 1)
                CacheResult(item, db, info);

            context.Response.StatusCode = info.status;
            context.Response.WriteAsync(info.msg, Encoding.UTF8);
        }
        #endregion

        #region 轮循请求
        /// <summary>
        /// 轮循请求
        /// </summary>
        private void Polling(ApiGatewayUrl item, HttpContext context,DataContext db, List<ApiGatewayDownParam> list)
        {
            var content = new StreamReader(context.Request.Body).ReadToEnd();
            var param = context.Request.QueryString.Value;

            var rand = new Random();
            var index = rand.Next(1, list.Count);
            var downparam = list[index];
            
            var info = GetReuslt(downparam, param, content,item.Key, item.IsLog,db);

            if (info.status != 200 && list.Count > 1)
            {
                var tempIndex = rand.Next(1, list.Count);
                while (index == tempIndex)
                {
                    tempIndex = rand.Next(1, list.Count);
                }

                downparam = list[tempIndex];
                info = GetReuslt(downparam, param, content,item.Key, item.IsLog,db);


                context.Response.StatusCode = info.status;
                context.Response.WriteAsync(info.msg, Encoding.UTF8);
            }
            else
            {
                //缓存结果
                if (item.IsCache==1)
                    CacheResult(item,db, info);

                context.Response.StatusCode = info.status;
                context.Response.WriteAsync(info.msg, Encoding.UTF8);
            }
        }
        #endregion

        #region 合并请求
        /// <summary>
        /// 合并请求
        /// </summary>
        /// <param name="item"></param>
        /// <param name="context"></param>
        private void Composite(ApiGatewayUrl item, HttpContext context,DataContext db, List<ApiGatewayDownParam> list)
        {
            var downDic = new Dictionary<string, object>();
            var result = new List<ReturnModel>();
            var task = new List<Task>();
            var content = new StreamReader(context.Request.Body).ReadToEnd();
            var param = context.Request.QueryString.Value;

            foreach (var downparam in list)
            {
                task.Add(Task.Factory.StartNew(() =>
                {
                    result.Add(GetReuslt(downparam, param, content,item.Key, item.IsLog,db));
                }));
            }

            Task.WaitAll(task.ToArray());

            var count = 0;
            foreach (var temp in result)
            {
                downDic.Add(string.Format("result{0}", count), BaseJson.JsonToDic(temp.msg));
                count++;
            }

            //缓存结果
            if (item.IsCache == 1)
                CacheResult(item, db, null, downDic);

            context.Response.StatusCode = 200;
            context.Response.WriteAsync(BaseJson.ModelToJson(downDic), Encoding.UTF8);
        }
        #endregion

        #region 获取token
        /// <summary>
        /// 获取token
        /// </summary>
        /// <param name="item"></param>
        /// <param name="context"></param>
        private void Token(HttpContext context,DataContext db)
        {
            var dic = new Dictionary<string, object>();
            var AppKey = "";
            var AppSecret = "";

            if (context.Request.Method.ToLower() == "get")
            {
                AppKey = context.Request.Query["AppKey"].ToStr().ToLower();
                AppSecret = context.Request.Query["AppSecret"].ToStr().ToLower();
            }
            else if (context.Request.Method.ToLower() == "post")
            {
                AppKey = context.Request.Form["AppKey"].ToStr().ToLower();
                AppSecret = context.Request.Form["AppSecret"].ToStr().ToLower();
            }
                                    
            if (FastRead.Query<ApiGatewayUser>(a => a.AppKey.ToLower() == AppKey.ToLower() && a.AppSecret.ToLower() == AppSecret.ToLower()).ToCount(db) <= 0)
            {
                context.Response.StatusCode = 200;
                dic.Add("success", false);
                dic.Add("result", "AppKey和AppSecret参数不存在");
                context.Response.WriteAsync(JsonConvert.SerializeObject(dic).ToString(), Encoding.UTF8);
            }
            else
            {
                var info = FastRead.Query<ApiGatewayUser>(a => a.AppKey.ToLower() == AppKey.ToLower() && a.AppSecret.ToLower() == AppSecret.ToLower()).ToItem<ApiGatewayUser>(db);
                info.Ip = GetClientIp(context);
                info.AccessExpires = DateTime.Now.AddHours(24).AddHours(8);
                info.AccessToken = BaseSymmetric.Generate(string.Format("{0}_{1}_{2}", info.AppKey, info.AppSecret, info.AccessExpires)).ToLower();

                //修改信息
                FastWrite.Update<ApiGatewayUser>(info,
                    a => a.AppKey.ToLower() == AppKey.ToLower() && a.AppSecret.ToLower() == AppSecret.ToLower()
                    , a => new { a.AccessExpires, a.Ip, a.AccessToken });

                dic.Add("success", true);
                dic.Add("AccessToken", info.AccessToken);
                dic.Add("AccessExpires", info.AccessExpires);

                context.Response.StatusCode = 200;
                context.Response.WriteAsync(JsonConvert.SerializeObject(dic).ToString(), Encoding.UTF8);
            }
        }
        #endregion

        #region 获取客户Ip
        /// <summary>
        /// 获取客户Ip
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public string GetClientIp(HttpContext context)
        {
            var ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (string.IsNullOrEmpty(ip))
            {
                ip = context.Connection.RemoteIpAddress.ToString();
            }
            return ip;
        }
        #endregion

        #region 缓存结果
        /// <summary>
        /// 缓存结果
        /// </summary>
        /// <param name="info"></param>
        /// <param name="item"></param>
        private void CacheResult(ApiGatewayUrl item, DataContext db,ReturnModel info = null, Dictionary<string, object> dic = null)
        {
            var model = new ApiGatewayCache();
            model.Key = item.Key.ToLower();
            model.TimeOut = DateTime.Now.AddDays(item.CacheTimeOut.ToStr().ToDouble(0)).AddHours(8);

            if (info != null)
                model.result = BaseJson.ModelToJson(info.msg);

            if (dic != null)
                model.result = BaseJson.ModelToJson(dic);

            if (FastRead.Query<ApiGatewayCache>(a => a.Key.ToUpper() == model.Key.ToUpper()).ToCount(db) <= 0)
                FastWrite.Add(model);
            else
                FastWrite.Update<ApiGatewayCache>(model, a => a.Key.ToUpper() == model.Key.ToUpper(), a => new { a.result, a.TimeOut });
        }
        #endregion
    }
}