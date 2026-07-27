using MaterialDesignThemes.Wpf;
using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using PracticalTools.GPG;
using PracticalTools.vo;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PracticalTools.Views
{
    /// <summary>
    /// GenerateKeyPair.xaml 的交互逻辑
    /// </summary>
    public partial class GenerateKeyPair : UserControl
    {
        private readonly GpgFileService _service;
        private bool _isPasswordVisible;

        public GenerateKeyPair()
        {
            InitializeComponent();
            _service = App.ServiceProvider.GetRequiredService<GpgFileService>();
        }

        private void SelectKeyt_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.PassphraseText = ((PasswordBox)sender).Password;
        }

        private void TogglePasswordVisibility_Click(object sender, RoutedEventArgs e)
        {
            _isPasswordVisible = !_isPasswordVisible;
            if (_isPasswordVisible)
            {
                SelectKeytText.Text = SelectKeytPassword.Password;
                SelectKeytPassword.Visibility = Visibility.Collapsed;
                SelectKeytText.Visibility = Visibility.Visible;
                EyeIcon.Kind = PackIconKind.EyeOff;
            }
            else
            {
                SelectKeytPassword.Password = SelectKeytText.Text;
                SelectKeytPassword.Visibility = Visibility.Visible;
                SelectKeytText.Visibility = Visibility.Collapsed;
                EyeIcon.Kind = PackIconKind.Eye;
            }
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
            var (pubRing, secRing, beginsOn, endsOn) = GpgHelper.GeneratePgpKeyPair(emailText, passphraseText);
            string secKeyAscii = GpgHelper.ExportPrivateKey(secRing);
            string pubKeyAscii = GpgHelper.ExportPublicKey(pubRing);

            GpgFile gpgFile = _service.GenerateGpgFile(emailText, passphraseText, secKeyAscii, pubKeyAscii, beginsOn, endsOn, "");
            int saved = _service.Save(gpgFile);
            dc.LoadGpgFileList();
            dc.WrapperSelectedChangedEvent();
            dc.GpgStatusMessage = "密钥对已生成";
            dc.GpgStatusColor = new SolidColorBrush(Colors.ForestGreen);
        }
    }
}