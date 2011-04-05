﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;
using NuGet;

namespace ICSharpCode.PackageManagement
{
	public abstract class PackagesViewModel : ViewModelBase<PackagesViewModel>, IDisposable
	{
		Pages pages = new Pages();
		
		IPackageManagementService packageManagementService;
		IPackageViewModelFactory packageViewModelFactory;
		ITaskFactory taskFactory;
		IEnumerable<IPackage> allPackages;
		ITask<PackagesForSelectedPageResult> task;
		
		public PackagesViewModel(
			IPackageManagementService packageManagementService,
			IMessageReporter messageReporter,
			ITaskFactory taskFactory)
			: this(
				packageManagementService,
				new LicenseAcceptanceService(),
				messageReporter,
				taskFactory)
		{
		}
		
		public PackagesViewModel(
			IPackageManagementService packageManagementService,
			ILicenseAcceptanceService licenseAcceptanceService,
			IMessageReporter messageReporter,
			ITaskFactory taskFactory)
			: this(
				packageManagementService, 
				new PackageViewModelFactory(packageManagementService, licenseAcceptanceService, messageReporter),
				taskFactory)
		{
		}
		
		public PackagesViewModel(
			IPackageManagementService packageManagementService, 
			IPackageViewModelFactory packageViewModelFactory,
			ITaskFactory taskFactory)
		{
			this.packageManagementService = packageManagementService;
			this.packageViewModelFactory = packageViewModelFactory;
			this.taskFactory = taskFactory;
			
			PackageViewModels = new ObservableCollection<PackageViewModel>();
			ErrorMessage = String.Empty;

			CreateCommands();
		}
		
		void CreateCommands()
		{
			ShowNextPageCommand = new DelegateCommand(param => ShowNextPage());
			ShowPreviousPageCommand = new DelegateCommand(param => ShowPreviousPage());
			ShowPageCommand = new DelegateCommand(param => ExecuteShowPageCommand(param));
			SearchCommand = new DelegateCommand(param => Search());
		}
		
		public ICommand ShowNextPageCommand { get; private set; }
		public ICommand ShowPreviousPageCommand { get; private set; }
		public ICommand ShowPageCommand { get; private set; }
		public ICommand SearchCommand { get; private set; }
		
		public void Dispose()
		{
			OnDispose();
			IsDisposed = true;
		}
		
		protected virtual void OnDispose()
		{
		}
		
		public bool IsDisposed { get; private set; }
		
		public bool HasError { get; private set; }
		public string ErrorMessage { get; private set; }
		
		public ObservableCollection<PackageViewModel> PackageViewModels { get; set; }
		
		public IPackageManagementService PackageManagementService { 
			get { return packageManagementService; }
		}
		
		public bool IsReadingPackages { get; private set; }
		
		public void ReadPackages()
		{
			allPackages = null;
			pages.SelectedPageNumber = 1;
			UpdateRepositoryBeforeReadPackagesTaskStarts();
			StartReadPackagesTask();
		}
		
		void StartReadPackagesTask()
		{
			IsReadingPackages = true;
			HasError = false;
			ClearPackages();
			CancelReadPackagesTask();
			CreateReadPackagesTask();
			task.Start();
		}
		
		protected virtual void UpdateRepositoryBeforeReadPackagesTaskStarts()
		{
		}
		
		void CancelReadPackagesTask()
		{
			if (task != null) {
				task.Cancel();
			}
		}
		
		void CreateReadPackagesTask()
		{
			task = taskFactory.CreateTask(
				() => GetPackagesForSelectedPageResult(),
				(result) => OnPackagesReadForSelectedPage(result));
		}
		
		PackagesForSelectedPageResult GetPackagesForSelectedPageResult()
		{
			IEnumerable<IPackage> packages = GetPackagesForSelectedPage();
			return new PackagesForSelectedPageResult(packages, TotalItems);
		}
		
		void OnPackagesReadForSelectedPage(ITask<PackagesForSelectedPageResult> task)
		{
			IsReadingPackages = false;
			if (task.IsFaulted) {
				SaveError(task.Exception);
			} else if (task.IsCancelled) {
				// Ignore
			} else {
				UpdatePackagesForSelectedPage(task.Result);
			}
			base.OnPropertyChanged(null);
		}
		
		protected void SaveError(AggregateException ex)
		{
			SaveError(ex.InnerException);
		}
		
		protected void SaveError(Exception ex)
		{
			HasError = true;
			ErrorMessage = ex.Message;
		}

		void UpdatePackagesForSelectedPage(PackagesForSelectedPageResult result)
		{			
			pages.TotalItems = result.TotalPackages;
			pages.TotalItemsOnSelectedPage = result.TotalPackagesOnPage;
			UpdatePackageViewModels(result.Packages);
		}
		
		void PagesChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			StartReadPackagesTask();
			base.OnPropertyChanged(null);
		}
		
		IEnumerable<IPackage> GetPackagesForSelectedPage()
		{
			IEnumerable<IPackage> filteredPackages = GetFilteredPackagesBeforePagingResults();
			return GetPackagesForSelectedPage(filteredPackages);
		}
		
		IEnumerable<IPackage> GetFilteredPackagesBeforePagingResults()
		{
			if (allPackages == null) {
				IQueryable<IPackage> packages = GetAllPackages();
				packages = OrderPackages(packages);
				packages = FilterPackagesBySearchCriteria(packages);
				TotalItems = packages.Count();
				allPackages = GetFilteredPackagesBeforePagingResults(packages);
			}
			return allPackages;
		}
		
		IQueryable<IPackage> OrderPackages(IQueryable<IPackage> packages)
		{
			return packages
				.OrderBy(package => package.Id);
		}
		
		IQueryable<IPackage> FilterPackagesBySearchCriteria(IQueryable<IPackage> packages)
		{
			string searchCriteria = GetSearchCriteria();
			return FilterPackagesBySearchCriteria(packages, searchCriteria);
		}
		
		string GetSearchCriteria()
		{
			if (String.IsNullOrWhiteSpace(SearchTerms)) {
				return null;
			}
			return SearchTerms;
		}

		protected virtual IQueryable<IPackage> FilterPackagesBySearchCriteria(IQueryable<IPackage> packages, string searchCriteria)
		{
			return packages.Find(searchCriteria);
		}
		
		IEnumerable<IPackage> GetPackagesForSelectedPage(IEnumerable<IPackage> allPackages)
		{
			int packagesToSkip = pages.ItemsBeforeFirstPage;
			return allPackages
				.Skip(packagesToSkip)
				.Take(pages.PageSize);
		}
		
		/// <summary>
		/// Returns all the packages.
		/// </summary>
		protected virtual IQueryable<IPackage> GetAllPackages()
		{
			return null;
		}
		
		/// <summary>
		/// Allows filtering of the packages before paging the results. Call base class method
		/// to run default filtering.
		/// </summary>
		protected virtual IEnumerable<IPackage> GetFilteredPackagesBeforePagingResults(IQueryable<IPackage> allPackages)
		{
			IEnumerable<IPackage> bufferedPackages = GetBufferedPackages(allPackages);
			return bufferedPackages;
		}
		
		IEnumerable<IPackage> GetBufferedPackages(IQueryable<IPackage> allPackages)
		{
			return allPackages.AsBufferedEnumerable(30);
		}
		
		void UpdatePackageViewModels(IEnumerable<IPackage> packages)
		{
			IEnumerable<PackageViewModel> currentViewModels = ConvertToPackageViewModels(packages);
			UpdatePackageViewModels(currentViewModels);
		}
		
		void UpdatePackageViewModels(IEnumerable<PackageViewModel> newPackageViewModels)
		{
			ClearPackages();
			PackageViewModels.AddRange(newPackageViewModels);
		}
		
		void ClearPackages()
		{
			PackageViewModels.Clear();
		}
		
		public IEnumerable<PackageViewModel> ConvertToPackageViewModels(IEnumerable<IPackage> packages)
		{
			foreach (IPackage package in packages) {
				yield return CreatePackageViewModel(package);
			}
		}
		
		PackageViewModel CreatePackageViewModel(IPackage package)
		{
			return packageViewModelFactory.CreatePackageViewModel(package);
		}
		
		public int SelectedPageNumber {
			get { return pages.SelectedPageNumber; }
			set {
				if (pages.SelectedPageNumber != value) {
					pages.SelectedPageNumber = value;
					StartReadPackagesTask();
					base.OnPropertyChanged(null);
				}
			}
		}
		
		public int PageSize {
			get { return pages.PageSize; }
			set { pages.PageSize = value;  }
		}
		
		public bool IsPaged {
			get { return pages.IsPaged; }
		}
				
		public ObservableCollection<Page> Pages {
			get { return pages; }
		}
		
		public bool HasPreviousPage {
			get { return pages.HasPreviousPage; }
		}
		
		public bool HasNextPage {
			get { return pages.HasNextPage; }
		}
		
		public int MaximumSelectablePages {
			get { return pages.MaximumSelectablePages; }
			set { pages.MaximumSelectablePages = value; }
		}
		
		public int TotalItems { get; private set; }
		
		public void ShowNextPage()
		{
			SelectedPageNumber += 1;
		}
		
		public void ShowPreviousPage()
		{
			SelectedPageNumber -= 1;
		}
		
		void ExecuteShowPageCommand(object param)
		{
			int pageNumber = (int)param;
			ShowPage(pageNumber);
		}
		
		public void ShowPage(int pageNumber)
		{
			SelectedPageNumber = pageNumber;
		}
		
		public bool IsSearchable { get; set; }
		
		public string SearchTerms { get; set; }
		
		public void Search()
		{
			ReadPackages();
			OnPropertyChanged(null);
		}
		
		public bool ShowPackageSources { get; set; }
		
		public IEnumerable<PackageSource> PackageSources {
			get { return packageManagementService.Options.PackageSources; }
		}
		
		public PackageSource SelectedPackageSource {
			get { return packageManagementService.ActivePackageSource; }
			set {
				if (packageManagementService.ActivePackageSource != value) {
					packageManagementService.ActivePackageSource = value;
					ReadPackages();
					OnPropertyChanged(null);
				}
			}
		}
	}
}
