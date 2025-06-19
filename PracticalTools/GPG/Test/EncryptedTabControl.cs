using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace PracticalTools.GPG.Test;

public partial class EncryptedTabControl : TabControl
{
    static EncryptedTabControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(EncryptedTabControl),
            new FrameworkPropertyMetadata(typeof(EncryptedTabControl)));
    }

    // 加密密钥依赖属性
    public static readonly DependencyProperty EncryptionKeyProperty =
        DependencyProperty.RegisterAttached("EncryptionKey", typeof(string),
            typeof(EncryptedTabControl), new PropertyMetadata(string.Empty));

    public static string GetEncryptionKey(DependencyObject obj)
    {
        return (string)obj.GetValue(EncryptionKeyProperty);
    }

    public static void SetEncryptionKey(DependencyObject obj, string value)
    {
        obj.SetValue(EncryptionKeyProperty, value);
    }

    // 加密内容依赖属性
    public static readonly DependencyProperty IsEncryptedProperty =
        DependencyProperty.RegisterAttached("IsEncrypted", typeof(bool),
            typeof(EncryptedTabControl), new PropertyMetadata(false, OnIsEncryptedChanged));

    public static bool GetIsEncrypted(DependencyObject obj)
    {
        return (bool)obj.GetValue(IsEncryptedProperty);
    }

    public static void SetIsEncrypted(DependencyObject obj, bool value)
    {
        obj.SetValue(IsEncryptedProperty, value);
    }

    private static void OnIsEncryptedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TabItem tabItem && (bool)e.NewValue)
        {
            // 存储原始内容引用
            tabItem.SetValue(RawContentProperty, tabItem.Content);

            // 加密内容
            if (tabItem.Content is FrameworkElement contentElement)
            {
                var encryptedContent = EncryptContent(contentElement, GetEncryptionKey(tabItem));
                tabItem.Content = new TextBlock
                {
                    Text = "🔒 Encrypted Content",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 16,
                    Foreground = Brushes.Gray
                };
                tabItem.SetValue(EncryptedContentProperty, encryptedContent);
            }
        }
    }

    // 原始内容存储
    private static readonly DependencyProperty RawContentProperty =
        DependencyProperty.RegisterAttached("RawContent", typeof(object),
            typeof(EncryptedTabControl), new PropertyMetadata(null));

    // 加密后内容存储
    private static readonly DependencyProperty EncryptedContentProperty =
        DependencyProperty.RegisterAttached("EncryptedContent", typeof(string),
            typeof(EncryptedTabControl), new PropertyMetadata(string.Empty));

    // 解密并显示内容
    public void DecryptAndShowContent(TabItem tabItem)
    {
        if (!GetIsEncrypted(tabItem)) return;

        var encryptedContent = (string)tabItem.GetValue(EncryptedContentProperty);
        var key = GetEncryptionKey(tabItem);

        if (!string.IsNullOrEmpty(encryptedContent) && !string.IsNullOrEmpty(key))
        {
            try
            {
                var decryptedContent = DecryptContent(encryptedContent, key);
                tabItem.Content = decryptedContent;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"解密失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // 加密内容
    private static string EncryptContent(FrameworkElement element, string key)
    {
        try
        {
            // 将元素转换为XAML字符串
            var xaml = XamlWriter.Save(element);

            // 使用AES加密
            using var aesAlg = Aes.Create();
            using var encryptor = aesAlg.CreateEncryptor(
                Encoding.UTF8.GetBytes(GetKeyBytes(key)),
                Encoding.UTF8.GetBytes(GetIvBytes(key)));

            using var msEncrypt = new MemoryStream();
            using var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write);
            using var swEncrypt = new StreamWriter(csEncrypt);

            swEncrypt.Write(xaml);
            swEncrypt.Flush();
            csEncrypt.FlushFinalBlock();

            return Convert.ToBase64String(msEncrypt.ToArray());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"加密失败: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return string.Empty;
        }
    }

    // 解密内容
    private static FrameworkElement DecryptContent(string encryptedContent, string key)
    {
        try
        {
            // 使用AES解密
            using var aesAlg = Aes.Create();
            using var decryptor = aesAlg.CreateDecryptor(
                Encoding.UTF8.GetBytes(GetKeyBytes(key)),
                Encoding.UTF8.GetBytes(GetIvBytes(key)));

            var cipherBytes = Convert.FromBase64String(encryptedContent);
            using var msDecrypt = new MemoryStream(cipherBytes);
            using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
            using var srDecrypt = new StreamReader(csDecrypt);

            var decryptedXaml = srDecrypt.ReadToEnd();

            // 将XAML字符串转换为元素
            return (FrameworkElement)XamlReader.Parse(decryptedXaml);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"解密失败: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }

    // 从密码生成密钥字节
    private static string GetKeyBytes(string password)
    {
        // 简单的密钥生成，实际应用中应使用更安全的方法
        var key = Encoding.UTF8.GetBytes(password.PadRight(32, 'x'));
        Array.Resize(ref key, 32); // AES-256
        return Convert.ToBase64String(key);
    }

    // 从密码生成IV字节
    private static string GetIvBytes(string password)
    {
        // 简单的IV生成，实际应用中应使用更安全的方法
        var iv = Encoding.UTF8.GetBytes(password.PadRight(16, 'x'));
        Array.Resize(ref iv, 16); // AES块大小
        return Convert.ToBase64String(iv);
    }

    // 重写选择逻辑
    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);

        if (SelectedItem is TabItem selectedTab)
        {
            DecryptAndShowContent(selectedTab);
        }
    }
}