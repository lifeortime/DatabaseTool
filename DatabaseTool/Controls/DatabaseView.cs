using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DatabaseTool.EventBus;
using DatabaseTool.ViewModel;
using DevExpress.Xpo;
using DevExpress.Xpo.DB;
using DevExpress.XtraTab;


namespace DatabaseTool.Controls
{
    public partial class DatabaseView : UserControl
    {
        public bool AllServer { get; private set; }
        public readonly Guid Id = Guid.NewGuid();

        private readonly DatabaseServer _databaseServer;
        private readonly XtraTabPage _tabPage;

        public DatabaseView()
        {
            InitializeComponent();
            EventBusFactory.Default.Subscribe<RemoteExecuteCommand>((cmd) =>
            {
                if (cmd.Sender == Id) return;
                else Execute(cmd.Sql, true);
            });
        }

        public DatabaseView(XtraTabPage tab, DatabaseServer databaseServer) : this()
        {
            _tabPage = tab;
            _databaseServer = databaseServer;
        }

        private void checkEdit_CheckStateChanged(object sender, EventArgs e)
        {
            AllServer = checkEdit.Checked;
        }

        private void btn_ExecuteAll_Click(object sender, EventArgs e)
        {
            Execute(txt_Sql.Text.Trim());
        }

        private void btn_ExecuteSelected_Click(object sender, EventArgs e)
        {
            Execute(txt_Sql.SelectedText.Trim());
        }

        private void Execute(string sql, bool remoteCommand = false)
        {
            try
            {
                if (string.IsNullOrEmpty(sql)) return;
                if (!remoteCommand) EventBusFactory.Default.Publish(new RemoteExecuteCommand(Id, sql));
                var session = new Session(_databaseServer.GetDataLayer());
                if (sql.StartsWith("select", StringComparison.OrdinalIgnoreCase))
                {
                    gridView.Columns.Clear();
                    XPDataView dv = new XPDataView();
                    SelectedData data = session.ExecuteQueryWithMetadata(sql);
                    foreach (var row in data.ResultSet[0].Rows)
                    {
                        dv.AddProperty((string)row.Values[0],
                            DBColumn.GetType((DBColumnType)Enum.Parse(typeof(DBColumnType), (string)row.Values[2])));
                    }
                    dv.LoadData(new SelectedData(data.ResultSet[1])); //如果包含多个结果将丢弃
                    gridControl.DataSource = dv;
                    PrintLog($"数据行数={dv.Count}");
                }

                else
                {
                    var count = session.ExecuteNonQuery(sql);
                    PrintLog($"受影响行数={count}");
                }
                session.Disconnect();
                _tabPage.Appearance.HeaderActive.BackColor = Color.Empty;
            }
            catch (Exception ex)
            {
                PrintLog($"{ex.Message}{Environment.NewLine}{ex.StackTrace}");
                _tabPage.Appearance.HeaderActive.BackColor = Color.Brown;
            }
        }

        private void PrintLog(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            LogHelper.Info(msg);
            // ReSharper disable once LocalizableElement
            txt_Log.Text += $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] - {msg}{Environment.NewLine}";
            txt_Log.SelectionStart = txt_Log.Text.Length;
            txt_Log.ScrollToCaret();
        }

        private void updateArea_Click(object sender, EventArgs e)
        {
            using (var session = new Session(_databaseServer.GetDataLayer()))
            {
                try
                {
                    var sql = "SELECT 编码,父级 from 基础_结构_区域";
                    var result = session.ExecuteQuery(sql);
                    PrintLog($"数据行数={result.ResultSet.Count()}");
                    var data = SelectDataToList(result);
                    //var filterData = data.Where(x => x.通道合计 <= 0 || x.设备合计 <= 0).ToList();
                    var filterSql = $@"SELECT 编码,父级 from 基础_结构_区域 where 编码 in (SELECT 区域编码 from 基础_通道 GROUP BY 区域编码) and (通道合计<=0 or 设备合计<=0 or 通道在线<=0 or 设备在线<=0)";
                    var filterData = session.ExecuteQuery(filterSql);
                    var filterList = SelectDataToList(filterData);
                    PrintLog($"存在通道设备合计<=0的数量：{filterList.Count}");
                    if (filterList.Count <= 0) return;
                    var total = new List<TreeNode>();//统计后的数据
                    var parentNode = new List<string>();
                    foreach (var item in filterList)
                    {
                        if (parentNode.Count <= 0)
                        {
                            GetParents(item.ParentId, data, ref parentNode);
                        }

                        var sqlStr = $@"SELECT nvl(max(通道合计),0) 通道合计,
nvl(max(通道在线),0) 通道在线,
nvl(max(通道异常),0) 通道异常,
nvl(max(设备合计),0) 设备合计,
nvl(max(设备在线),0) 设备在线,
nvl(max(设备异常),0) 设备异常 from (
SELECT count(0) 通道合计,sum(case when 综合状态=2 then 1 else 0 end) 通道在线,sum(case when 综合状态=1 then 1 else 0 end) 通道异常,0 设备合计,0 设备在线,0 设备异常 from 基础_通道 where 区域编码='{item.Id}'
UNION
SELECT 0 通道合计,0 通道在线,0 通道异常,count(0) 设备合计,sum(case when 综合状态=2 then 1 else 0 end) 设备在线,sum(case when 综合状态=1 then 1 else 0 end) 设备异常 from 基础_设备 where 区域编码='{item.Id}') a
";//
                        var childCount = session.ExecuteQuery(sqlStr);
                        if (childCount.ResultSet.Count() > 0)
                        {
                            var channelTotal = Convert.ToInt32(childCount.ResultSet[0].Rows[0].Values[0]);
                            var channelOnline = Convert.ToInt32(childCount.ResultSet[0].Rows[0].Values[1]);
                            var channelBroken = Convert.ToInt32(childCount.ResultSet[0].Rows[0].Values[2]);
                            var deviceTotal = Convert.ToInt32(childCount.ResultSet[0].Rows[0].Values[3]);
                            var deviceOnline = Convert.ToInt32(childCount.ResultSet[0].Rows[0].Values[4]);
                            var deviceBroken = Convert.ToInt32(childCount.ResultSet[0].Rows[0].Values[5]);
                            if (channelTotal > 0 || deviceTotal > 0 || channelOnline > 0 || channelBroken > 0 || deviceOnline > 0 || deviceBroken > 0)
                            {
                                PrintLog($"区域编码：{item.Id},通道合计：{channelTotal},设备合计：{deviceTotal},通道在线：{channelOnline},通道异常：{channelBroken},设备在线：{deviceOnline},设备异常：{deviceBroken}");
                                var node = new TreeNode(item.Id, item.ParentId, channelTotal, channelOnline, channelBroken, deviceTotal, deviceOnline, deviceBroken);
                                total.Add(node);
                                var updateSql = $@"update 基础_结构_区域 set 通道合计={channelTotal},通道异常={channelBroken},通道在线={channelOnline},
                                        设备合计={deviceTotal},设备异常={deviceBroken},设备在线={deviceOnline} where 编码='{item.Id}'";
                                var count = session.ExecuteNonQuery(updateSql);
                                PrintLog($"更新区域【{item.Id}】的通道设备合计，受影响行数={count}");
                            }
                            else
                            {
                                PrintLog($"该区域编码【{item.Id}】下，通道设备合计为0");
                            }
                        }
                        else
                        {
                            PrintLog($"该区域编码【{item.Id}】下，通道设备合计为0");
                        }
                    }
                    foreach (var item in parentNode)
                    {
                        if (total.Count > 0)
                        {
                            var channelTotal = total.Sum(x => x.通道合计);
                            var channelOnline = total.Sum(x => x.通道在线);
                            var channelBroken = total.Sum(x => x.通道异常);
                            var deviceTotal = total.Sum(x => x.设备合计);
                            var deviceOnline = total.Sum(x => x.设备在线);
                            var deviceBroken = total.Sum(x => x.设备异常);
                            PrintLog($"区域编码：{item},通道合计：{channelTotal},设备合计：{deviceTotal},通道在线：{channelOnline},通道异常：{channelBroken},设备在线：{deviceOnline},设备异常：{deviceBroken}");
                            var updateSql = $@"update 基础_结构_区域 set 通道合计={channelTotal},通道异常={channelBroken},通道在线={channelOnline},
                                        设备合计={deviceTotal},设备异常={deviceBroken},设备在线={deviceOnline} where 编码='{item}'";
                            var count = session.ExecuteNonQuery(updateSql);
                            PrintLog($"更新区域【{item}】的通道设备合计，受影响行数={count}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    PrintLog($"{ex.Message}{Environment.NewLine}{ex.StackTrace}");
                    PrintLog(ex.ToString());
                    _tabPage.Appearance.HeaderActive.BackColor = Color.Brown;
                }
            }
        }

        private List<TreeNode> SelectDataToList(SelectedData selectedData)
        {
            var list = new List<TreeNode>();
            if (selectedData.ResultSet.Count() > 0)
            {
                foreach (var item in selectedData.ResultSet[0].Rows)
                {
                    list.Add(new TreeNode()
                    {
                        Id = item.Values[0].ToString(),
                        ParentId = item.Values[1].ToString()
                    });
                }
            }
            return list;
        }

        /// <summary>
        /// 递归查询子级ID,包含自己
        /// </summary>
        /// <param name="id">ID</param>
        /// <param name="list"></param>
        /// <returns></returns>
        private void GetChilds(string id, List<TreeNode> list, ref List<string> childNode)
        {
            childNode.Add(id);
            List<TreeNode> result = list.Where(x => x.ParentId == id).ToList();
            if (result.Count > 0) GetChilds(result[0].Id, list, ref childNode);
        }

        /// <summary>
        /// 递归查询父级ID
        /// </summary>
        /// <param name="parentId">parentId</param>
        /// <param name="list"></param>
        /// <returns></returns>
        private void GetParents(string parentId, List<TreeNode> list, ref List<string> parentNode)
        {
            List<TreeNode> result = list.Where(x => x.Id == parentId).ToList();
            if (result.Count > 0)
            {
                parentNode.Add(parentId);
                GetParents(result[0].ParentId, list, ref parentNode);
            }
        }

        public class TreeNode
        {
            public TreeNode()
            {
            }
            public TreeNode(string id, string parentId, int channelTotal = 0, int channelOnline = 0, int channelBroken = 0, int deviceTotal = 0, int deviceOnline = 0, int deviceBroken = 0)
            {
                Id = id;
                ParentId = parentId;
                通道合计 = channelTotal;
                通道在线 = channelOnline;
                通道异常 = channelBroken;
                设备合计 = deviceTotal;
                设备在线 = deviceOnline;
                设备异常 = deviceBroken;
            }

            public string Id { get; set; }
            public string ParentId { get; set; }
            //public string Code { get; set; }
            public int 通道合计 { get; set; }
            public int 设备合计 { get; set; }
            public int 设备在线 { get; set; }
            public int 设备异常 { get; set; }
            public int 通道在线 { get; set; }
            public int 通道异常 { get; set; }
        }

    }
}
