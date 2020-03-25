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
                    var sql = "SELECT 编码,父级,代码,通道合计,设备合计 from 基础_结构_区域";
                    var result = session.ExecuteQuery(sql);
                    PrintLog($"数据行数={result.ResultSet.Count()}");
                    var data = SelectDataToList(result);
                    //var filterData = data.Where(x => x.通道合计 <= 0 || x.设备合计 <= 0).ToList();
                    var filterSql = $@"SELECT 编码,父级,代码,通道合计,设备合计 from 基础_结构_区域 where 编码 in (SELECT 区域编码 from 基础_通道 GROUP BY 区域编码) and (通道合计<=0 or 设备合计<=0)";
                    var filterData = session.ExecuteQuery(filterSql);
                    var filterList = SelectDataToList(filterData);
                    PrintLog($"存在通道设备合计<=0的数量：{filterList.Count}");
                    if (filterList.Count <= 0) return;
                    var total = new List<TreeNode>();//统计后的数据
                    foreach (var item in filterList)
                    {
                        var id = GetChilds(item.Id, data);//
                        var childTotal = new List<TreeNode>();//当前子级的统计
                        foreach (var o in childNode)
                        {
                            if (!total.Exists(x => x.Id == o))
                            {
                                var sqlStr = $@"SELECT max(通道合计) 通道合计,max(设备合计) 设备合计 from (
SELECT count(0) 通道合计,0 设备合计 from 基础_通道 where 区域编码='{o}'
UNION
SELECT 0 通道合计,count(0) 设备合计 from 基础_设备 where 区域编码='{o}') a";//
                                var childCount = session.ExecuteQuery(sqlStr);
                                if (childCount.ResultSet.Count() > 0)
                                {
                                    var channelTotal = Convert.ToInt32(childCount.ResultSet[0].Rows[0].Values[0]);
                                    var deviceTotal = Convert.ToInt32(childCount.ResultSet[0].Rows[0].Values[1]);
                                    if (channelTotal > 0 || deviceTotal > 0)
                                    {
                                        PrintLog($"区域编码：{o},通道合计：{channelTotal},设备合计：{deviceTotal}");
                                        total.Add(new TreeNode()
                                        {
                                            Id = o,
                                            ParentId = item.Id,
                                            通道合计 = channelTotal,
                                            设备合计 = deviceTotal
                                        });
                                        childTotal.Add(new TreeNode()
                                        {
                                            Id = o,
                                            ParentId = item.Id,
                                            通道合计 = channelTotal,
                                            设备合计 = deviceTotal
                                        });
                                        if (o == item.Id)
                                        {
                                            channelTotal += childTotal.Sum(x => x.通道合计);
                                            deviceTotal += childTotal.Sum(x => x.设备合计);
                                        }
                                        var updateSql = $@"update 基础_结构_区域 set 通道合计={channelTotal},设备合计={deviceTotal} where 编码='{o}'";
                                        var count = session.ExecuteNonQuery(updateSql);
                                        PrintLog($"更新区域【{o}】的通道设备合计，受影响行数={count}");
                                    }
                                    else
                                    {
                                        PrintLog($"该区域编码【{o}】下，通道设备合计为0");
                                    }
                                }
                                else
                                {
                                    PrintLog($"该区域编码【{o}】下，通道设备合计为0");
                                }
                            }
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
                        ParentId = item.Values[1].ToString(),
                        通道合计 = Convert.ToInt32(item.Values[3]),
                        设备合计 = Convert.ToInt32(item.Values[4]),
                        Code = item.Values[2].ToString()
                    });
                }
            }
            return list;
        }
        List<string> childNode = new List<string>();
        /// <summary>
        /// 递归查询子级ID,包含自己
        /// </summary>
        /// <param name="id">ID</param>
        /// <param name="list"></param>
        /// <returns></returns>
        private string GetChilds(string id, List<TreeNode> list)
        {
            childNode.Add(id);
            List<TreeNode> result = list.Where(x => x.ParentId == id).ToList();
            if (result.Count > 0)
                return GetChilds(result[0].Id, list);
            else
                return id;
        }

        public class TreeNode
        {
            public string Id { get; set; }
            public string ParentId { get; set; }
            public string Code { get; set; }
            public int 通道合计 { get; set; }
            public int 设备合计 { get; set; }
        }

    }
}
