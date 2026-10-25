using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SchoolWidget
{
    sealed class FeedConnectionDialog : Form
    {
        bool working;
        public FeedConnectionDialog(FeedSync feed)
        {
            Text="학교 일정 연결";Font=new Font("Malgun Gothic",9f);ClientSize=new Size(530,365);
            FormBorderStyle=FormBorderStyle.FixedDialog;StartPosition=FormStartPosition.CenterScreen;
            MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;
            var intro=new Label{Left=20,Top=18,Width=490,Height=42,Text="학교에서 안내받은 일정 연결 주소를 입력하세요.\n연결한 학교의 최신 일정을 자동으로 받아옵니다."};
            var url=new TextBox{Left=20,Top=89,Width=490,Text=feed.Url,MaxLength=2048};
            var token=new TextBox{Left=20,Top=153,Width=490,UseSystemPasswordChar=true,MaxLength=4096};
            try{token.Text=feed.GetToken();}catch(Exception){}
            var state=new Label{Left=20,Top=201,Width=490,Height=67,Text=(feed.Name??"연결된 학교 없음")+"\n"+feed.Status+"\n앱 시작 시 · 5분마다 자동 갱신 / 개인 데이터는 PC에 보관"};
            var connect=new Button{Left=20,Top=282,Width=130,Height=30,Text="확인 후 연결"};
            var refresh=new Button{Left=158,Top=282,Width=100,Height=30,Text="새로고침"};
            var disconnect=new Button{Left=266,Top=282,Width=100,Height=30,Text="연결 해제"};
            var import=new Button{Left=20,Top=323,Width=180,Text="로컬 일정 JSON 가져오기"};
            var close=new Button{Left=410,Top=323,Width=100,Text="닫기",DialogResult=DialogResult.Cancel};
            Controls.AddRange(new Control[]{intro,new Label{Left=20,Top=66,Width=250,Text="일정 연결 주소 (HTTPS)"},url,
                new Label{Left=20,Top=130,Width=460,Text="읽기 토큰 (학교에서 안내한 경우에만 입력)"},token,state,connect,refresh,disconnect,import,close});
            Action<bool> busy=value=>{working=value;connect.Enabled=refresh.Enabled=disconnect.Enabled=import.Enabled=close.Enabled=!value;url.Enabled=token.Enabled=!value;};
            connect.Click+=async(s,e)=>{
                busy(true);state.Text="서버와 데이터 형식을 확인하고 있습니다…";
                try{await feed.ConnectAsync(url.Text,token.Text);url.Text=feed.Url;state.Text=feed.Name+"\n"+feed.Status;}
                catch(Exception ex){state.Text="연결하지 못했습니다. 기존 일정은 유지됩니다.\n"+ex.Message;}
                finally{busy(false);}
            };
            refresh.Click+=async(s,e)=>{busy(true);try{await feed.RefreshAsync();state.Text=(feed.Name??"학교 미연결")+"\n"+feed.Status;}finally{busy(false);}};
            disconnect.Click+=(s,e)=>NativeDialogs.Safe(()=>{feed.Disconnect();url.Clear();token.Clear();state.Text="연결 해제됨 · 개인 메모와 할 일은 유지됩니다.";});
            import.Click+=(s,e)=>{using(var file=new OpenFileDialog{Filter="학사일정 JSON|*.json"})if(file.ShowDialog(this)==DialogResult.OK)
                NativeDialogs.Safe(()=>{feed.ImportLocal(File.ReadAllText(file.FileName,Encoding.UTF8));url.Clear();token.Clear();state.Text=feed.Name+"\n"+feed.Status;});};
            FormClosing+=(s,e)=>{if(working)e.Cancel=true;};AcceptButton=connect;CancelButton=close;
        }
    }
}
