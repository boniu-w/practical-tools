using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using PracticalTools.GPG;
using PracticalTools.vo;
using System.Windows;
using System.Windows.Controls;

namespace PracticalTools.Views
{
    /// <summary>
    /// GenerateKeyPair.xaml 的交互逻辑
    /// </summary>
    public partial class GenerateKeyPair : UserControl
    {
        private readonly GpgFileService _service;

        public GenerateKeyPair()
        {
            InitializeComponent();
            _service = App.ServiceProvider.GetRequiredService<GpgFileService>();
        }

        /// <summary>
        /// 生成密钥对并保存到数据库
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void GeneratreKeyPairStr(object sender, RoutedEventArgs e)
        {
            var dc = DataContext as MainViewModel;
            var emailText = dc.EmailText;
            var passphraseText = dc.PassphraseText;
            var (pubRing, secRing, beginsOn, endsOn) = GpgHelper.GeneratePgpKeyPair(passphraseText, emailText);
            string secKeyAscii = GpgHelper.ExportPrivateKey(secRing);
            string pubKeyAscii = GpgHelper.ExportPublicKey(pubRing);

            GpgFile gpgFile = _service.GenerateGpgFile(emailText, emailText, secKeyAscii, pubKeyAscii, beginsOn, endsOn, "");
            int saved = _service.Save(gpgFile);
            dc.GpgFileWrapperList = new ObservableCollection<GpgFileEntryWrapper>(dc._myDbContext.GpgFile.Select(e => new GpgFileEntryWrapper(e)));
        }
    }
}
