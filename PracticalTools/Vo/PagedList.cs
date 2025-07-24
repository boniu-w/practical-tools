using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PracticalTools.vo;

public class PagedList<T> : ObservableCollection<T>, INotifyPropertyChanged where T : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged(string name)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    
    public ObservableCollection<T> SourceList { get; set; }
    private int _pageSize = 10;

    public int PageSize
    {
        get => _pageSize;
        set
        {
            if (value > 0)
            {
                _pageSize = value;
                GoToPage(1); // 重设到第一页
            }
        }
    }

    private int _currentPage = 1;
    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (_currentPage != value)
            {
                _currentPage = value;
                OnPropertyChanged(nameof(CurrentPage));
                OnPropertyChanged(nameof(PageCount)); // 可选
            }
        }
    }

    public int PageCount =>
        (int)Math.Ceiling((double)(SourceList?.Count ?? 0) / PageSize);

    public void Refresh()
    {
        this.Clear();
        foreach (var old in this)
            old.PropertyChanged -= OnItemPropertyChanged;
        
        if (SourceList == null) return;
        int start = (CurrentPage - 1) * PageSize;
        int end = Math.Min(start + PageSize, SourceList.Count);
       
        for (int i = start; i < end; i++)
        {
            var item = SourceList[i];
            this.Add(item);
            item.PropertyChanged += OnItemPropertyChanged;
        }
        
        OnPropertyChanged(nameof(IsPageAllSelected));
    }

    public void GoToPage(int page)
    {
        if (page < 1 || page > PageCount) return;
        CurrentPage = page;
        Refresh();
    }

    public void NextPage() => GoToPage(CurrentPage + 1);
    public void PreviousPage() => GoToPage(CurrentPage - 1);
    public void FirstPage() => GoToPage(1);
    public void LastPage() => GoToPage(PageCount);
    
    public bool IsPageAllSelected
    {
        get => this.Count > 0 && this.All(IsSelected);
        set
        {
            foreach (var item in this)
                SetSelected(item, value);

            OnPropertyChanged(nameof(IsPageAllSelected));
        }
    }

    private bool IsSelected(T item)
    {
        var prop = typeof(T).GetProperty("IsSelected");
        return prop != null && prop.GetValue(item) is bool b && b;
    }

    private void SetSelected(T item, bool value)
    {
        var prop = typeof(T).GetProperty("IsSelected");
        prop?.SetValue(item, value);
    }
    
    private void OnItemPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "IsSelected")
            OnPropertyChanged(nameof(IsPageAllSelected));
    }
}