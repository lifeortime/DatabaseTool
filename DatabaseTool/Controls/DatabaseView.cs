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
                    var sql = "SELECT 编码,父级,通道合计,设备合计 from 基础_结构_区域";
                    var result = session.ExecuteQuery(sql);
                    PrintLog($"数据行数={result.ResultSet.Count()}");
                    foreach (var item in result.ResultSet[0].Rows)
                    {
                        var updateSql = $@"Update 基础_结构_区域 set 通道合计=(select count(0) from 基础_通道 where 区域编码='{item.Values[0]}')
                        ,设备合计=(select count(1) from 基础_设备 where 区域编码='{item.Values[0]}') where 编码='{item.Values[0]}'";
                        var count = session.ExecuteNonQuery(updateSql);
                        PrintLog($"更新：{count}");
                    }
                    result = session.ExecuteQuery(sql);
                    var srcRegions = SelectDataToList(result);

                    var root = srcRegions.GenerateTree(c => c.Id, c => c.ParentId, "0").FirstOrDefault();
                    if (root == null)
                    {
                        PrintLog("未找到 编码=0 的根节点");
                        return;
                    }

                    root.Recursive((p, c) =>//重新计算整个树结构上的 设备合计、通道合计
                    {
                        p.设备合计 += c.设备合计;
                        p.通道合计 += c.通道合计;

                        var updateSql = $@"update 基础_结构_区域 set 通道合计={p.通道合计},
                                        设备合计={p.设备合计} where 编码='{p.Id}'";

                        var count = session.ExecuteNonQuery(updateSql);
                        PrintLog($"更新区域【{p.Id}】的通道设备合计，受影响行数={count}");
                    });
                    PrintLog("区域 通道合计 与 设备合计 数量已重新计算");
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
                    if (Convert.ToInt32(item.Values[2]) > 0)
                    {

                    }
                    list.Add(new TreeNode()
                    {
                        Id = item.Values[0].ToString(),
                        ParentId = item.Values[1].ToString(),
                        通道合计 = Convert.ToInt32(item.Values[2]),
                        设备合计 = Convert.ToInt32(item.Values[3])
                    });
                }
            }
            return list;
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
