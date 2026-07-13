// Copyright (c) 2014 AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Windows;
using System.Windows.Input;
using ICSharpCode.WpfDesign.Adorners;
using ICSharpCode.WpfDesign.Designer.Controls;
using ICSharpCode.WpfDesign.Extensions;

namespace ICSharpCode.WpfDesign.Designer.Extensions
{
	/// <summary>
	/// A typed resize-thumb gesture backed by the designer placement transaction.
	/// </summary>
	/// <remarks>
	/// <see cref="Update"/> accepts the cumulative pointer delta in the adorned
	/// element's local coordinate space. The routed thumb input path uses this same
	/// contract after applying the element transform.
	/// </remarks>
	public interface IResizeThumbGesture
	{
		/// <summary>Gets the thumb alignment that controls the resize anchors.</summary>
		PlacementAlignment Alignment { get; }

		/// <summary>Gets whether the gesture can still be updated, completed, or canceled.</summary>
		bool IsActive { get; }

		/// <summary>Gets whether the gesture applied a size or placement change.</summary>
		bool HasResized { get; }

		/// <summary>
		/// Applies a cumulative local-space pointer delta to the resize operation.
		/// </summary>
		/// <param name="delta">The cumulative pointer delta since the gesture started.</param>
		/// <param name="preserveAspectRatio">
		/// Whether a corner resize should use the routed Control-key aspect constraint.
		/// </param>
		/// <returns><see langword="false"/> when the gesture is inactive or the delta is not finite.</returns>
		bool Update(Vector delta, bool preserveAspectRatio);

		/// <summary>Commits the gesture as one placement transaction.</summary>
		void Complete();

		/// <summary>Aborts the gesture and restores its original placement.</summary>
		void Cancel();
	}

	/// <summary>
	/// The resize thumb around a component.
	/// </summary>
	[ExtensionServer(typeof(OnlyOneItemSelectedExtensionServer))]
	[ExtensionFor(typeof(FrameworkElement))]
	public sealed class ResizeThumbExtension : SelectionAdornerProvider
	{
		readonly AdornerPanel adornerPanel;
		readonly DesignerThumb[] _designerThumbs;
		/// <summary>An array containing this.ExtendedItem as only element</summary>
		readonly DesignItem[] extendedItemArray = new DesignItem[1];
		IPlacementBehavior resizeBehavior;
		ResizeThumbGesture activeGesture;
		
		/// <summary>
		/// Gets whether this extension is resizing any element.
		/// </summary>
		public bool IsResizing{
			get { return activeGesture != null; }
		}
		
		public ResizeThumbExtension()
		{
			adornerPanel = new AdornerPanel();
			adornerPanel.Order = AdornerOrder.Foreground;
			this.Adorners.Add(adornerPanel);
			
			_designerThumbs = new DesignerThumb[] {
				CreateThumb(PlacementAlignment.TopLeft, Cursors.SizeNWSE),
				CreateThumb(PlacementAlignment.Top, Cursors.SizeNS),
				CreateThumb(PlacementAlignment.TopRight, Cursors.SizeNESW),
				CreateThumb(PlacementAlignment.Left, Cursors.SizeWE),
				CreateThumb(PlacementAlignment.Right, Cursors.SizeWE),
				CreateThumb(PlacementAlignment.BottomLeft, Cursors.SizeNESW),
				CreateThumb(PlacementAlignment.Bottom, Cursors.SizeNS),
				CreateThumb(PlacementAlignment.BottomRight, Cursors.SizeNWSE)
			};
		}
		
		DesignerThumb CreateThumb(PlacementAlignment alignment, Cursor cursor)
		{
			DesignerThumb designerThumb = new ResizeThumb( cursor == Cursors.SizeNS, cursor == Cursors.SizeWE );
			designerThumb.Cursor = cursor;
			designerThumb.Alignment = alignment;
			AdornerPanel.SetPlacement(designerThumb, Place(ref designerThumb, alignment));
			adornerPanel.Children.Add(designerThumb);
			
			DragListener drag = new DragListener(designerThumb);
			drag.Started += new DragHandler(drag_Started);
			drag.Changed += new DragHandler(drag_Changed);
			drag.Completed += new DragHandler(drag_Completed);
			return designerThumb;
		}
		
		/// <summary>
		/// Places resize thumbs at their respective positions
		/// and streches out thumbs which are at the center of outline to extend resizability across the whole outline
		/// </summary>
		/// <param name="designerThumb"></param>
		/// <param name="alignment"></param>
		/// <returns></returns>
		private RelativePlacement Place(ref DesignerThumb designerThumb,PlacementAlignment alignment)
		{
			RelativePlacement placement = new RelativePlacement(alignment.Horizontal,alignment.Vertical);
			
			if (alignment.Horizontal == HorizontalAlignment.Center)
			{
				placement.WidthRelativeToContentWidth = 1;
				placement.HeightOffset = 6;
				designerThumb.Opacity = 0;
				return placement;
			}
			if (alignment.Vertical == VerticalAlignment.Center)
			{
				placement.HeightRelativeToContentHeight = 1;
				placement.WidthOffset = 6;
				designerThumb.Opacity = 0;
				return placement;
			}
			
			placement.WidthOffset = 6;
			placement.HeightOffset = 6;
			return placement;
		}

		// TODO : Remove all hide/show extensions from here.
		void drag_Started(DragListener drag)
		{
			/* Abort editing Text if it was editing, because it interferes with the undo stack. */
			//foreach(var extension in this.ExtendedItem.Extensions){
			//	if(extension is InPlaceEditorExtension){
			//		((InPlaceEditorExtension)extension).AbortEdit();
			//	}
			//}
			
			drag.Transform = this.ExtendedItem.GetCompleteAppliedTransformationToView();
			var thumb = drag.Target as DesignerThumb;
			if (thumb != null)
				TryStartGesture(thumb.Alignment);
		}

		void drag_Changed(DragListener drag)
		{
			if (activeGesture != null) {
				bool preserveAspectRatio = Keyboard.IsKeyDown(Key.LeftCtrl)
					|| Keyboard.IsKeyDown(Key.RightCtrl);
				activeGesture.Update(drag.Delta, preserveAspectRatio);
			}
		}

		void drag_Completed(DragListener drag)
		{
			if (activeGesture == null)
				return;

			if (drag.IsCanceled)
				activeGesture.Cancel();
			else
				activeGesture.Complete();
		}

		/// <summary>
		/// Starts a typed resize gesture for one of the eight resize-thumb alignments.
		/// </summary>
		/// <returns>
		/// The active gesture, or <see langword="null"/> when this extension is already
		/// resizing, the alignment is not a resize handle, or placement is unavailable.
		/// </returns>
		public IResizeThumbGesture TryStartGesture(PlacementAlignment alignment)
		{
			if (activeGesture != null
				|| !IsResizeAlignment(alignment)
				|| resizeBehavior == null
				|| !resizeBehavior.CanPlace(extendedItemArray, PlacementType.Resize, alignment))
				return null;

			ResizeThumbGesture gesture;
			try {
				gesture = new ResizeThumbGesture(this, alignment);
			} catch (PlacementOperation.PlacementOperationException) {
				return null;
			}

			activeGesture = gesture;
			ShowSizeAndHideHandles();
			return gesture;
		}

		static bool IsResizeAlignment(PlacementAlignment alignment)
		{
			return alignment != PlacementAlignment.Center
				&& alignment.Horizontal != HorizontalAlignment.Stretch
				&& alignment.Vertical != VerticalAlignment.Stretch;
		}

		void EndGesture(ResizeThumbGesture gesture)
		{
			if (!ReferenceEquals(activeGesture, gesture))
				return;

			activeGesture = null;
			HideSizeAndShowHandles();
		}

		sealed class ResizeThumbGesture : IResizeThumbGesture
		{
			readonly ResizeThumbExtension owner;
			readonly PlacementOperation operation;
			readonly Size oldSize;
			bool isActive = true;
			bool hasResized;

			internal ResizeThumbGesture(ResizeThumbExtension owner, PlacementAlignment alignment)
			{
				this.owner = owner;
				Alignment = alignment;
				oldSize = new Size(
					ModelTools.GetWidth(owner.ExtendedItem.View),
					ModelTools.GetHeight(owner.ExtendedItem.View));
				operation = PlacementOperation.Start(owner.extendedItemArray, PlacementType.Resize);
			}

			public PlacementAlignment Alignment { get; private set; }

			public bool IsActive {
				get { return isActive; }
			}

			public bool HasResized {
				get { return hasResized; }
			}

			public bool Update(Vector delta, bool preserveAspectRatio)
			{
				if (!isActive || !IsFinite(delta))
					return false;

				double dx = 0;
				double dy = 0;
				if (Alignment.Horizontal == HorizontalAlignment.Left)
					dx = -delta.X;
				if (Alignment.Horizontal == HorizontalAlignment.Right)
					dx = delta.X;
				if (Alignment.Vertical == VerticalAlignment.Top)
					dy = -delta.Y;
				if (Alignment.Vertical == VerticalAlignment.Bottom)
					dy = delta.Y;

				if (preserveAspectRatio
					&& Alignment.Horizontal != HorizontalAlignment.Center
					&& Alignment.Vertical != VerticalAlignment.Center) {
					if (dx > dy)
						dx = dy;
					else
						dy = dx;
				}

				FrameworkElement element = owner.ExtendedItem.View as FrameworkElement;
				double newWidth = ConstrainDimension(
					oldSize.Width + dx,
					element != null ? element.MinWidth : 0,
					element != null ? element.MaxWidth : double.PositiveInfinity);
				double newHeight = ConstrainDimension(
					oldSize.Height + dy,
					element != null ? element.MinHeight : 0,
					element != null ? element.MaxHeight : double.PositiveInfinity);

				PlacementInformation info = operation.PlacedItems[0];
				Rect result = ResizeFromOriginalBounds(
					info.OriginalBounds,
					newWidth,
					newHeight,
					Alignment);
				info.Bounds = result.Round();
				info.ResizeThumbAlignment = Alignment;
				operation.CurrentContainerBehavior.BeforeSetPosition(operation);
				info.Bounds = ConstrainBounds(info.Bounds, element, Alignment);
				operation.CurrentContainerBehavior.SetPosition(info);
				hasResized = hasResized || info.Bounds != info.OriginalBounds;
				return true;
			}

			public void Complete()
			{
				if (!isActive)
					return;

				Finish();
				if (hasResized)
					operation.Commit();
				else
					operation.Abort();
			}

			public void Cancel()
			{
				if (!isActive)
					return;

				Finish();
				operation.Abort();
			}

			void Finish()
			{
				isActive = false;
				owner.EndGesture(this);
			}

			static bool IsFinite(Vector vector)
			{
				return !double.IsNaN(vector.X)
					&& !double.IsInfinity(vector.X)
					&& !double.IsNaN(vector.Y)
					&& !double.IsInfinity(vector.Y);
			}

			static double ConstrainDimension(double value, double minimum, double maximum)
			{
				return Math.Min(Math.Max(Math.Max(0, value), minimum), maximum);
			}

			static Rect ResizeFromOriginalBounds(
				Rect original,
				double width,
				double height,
				PlacementAlignment alignment)
			{
				Rect result = original;
				if (alignment.Horizontal == HorizontalAlignment.Left)
					result.X = original.Right - width;
				if (alignment.Vertical == VerticalAlignment.Top)
					result.Y = original.Bottom - height;
				result.Width = width;
				result.Height = height;
				return result;
			}

			static Rect ConstrainBounds(
				Rect bounds,
				FrameworkElement element,
				PlacementAlignment alignment)
			{
				if (element == null)
					return bounds;

				double right = bounds.Right;
				double bottom = bounds.Bottom;
				bounds.Width = ConstrainDimension(bounds.Width, element.MinWidth, element.MaxWidth);
				bounds.Height = ConstrainDimension(bounds.Height, element.MinHeight, element.MaxHeight);
				if (alignment.Horizontal == HorizontalAlignment.Left)
					bounds.X = right - bounds.Width;
				if (alignment.Vertical == VerticalAlignment.Top)
					bounds.Y = bottom - bounds.Height;
				return bounds;
			}
		}
		
		protected override void OnInitialized()
		{
			base.OnInitialized();
			extendedItemArray[0] = this.ExtendedItem;
			this.ExtendedItem.PropertyChanged += OnPropertyChanged;
			this.Services.Selection.PrimarySelectionChanged += OnPrimarySelectionChanged;
			resizeBehavior = PlacementOperation.GetPlacementBehavior(extendedItemArray);
			UpdateAdornerVisibility();
			OnPrimarySelectionChanged(null, null);
		}
		
		void OnPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			UpdateAdornerVisibility();
		}
		
		protected override void OnRemove()
		{
			if (activeGesture != null)
				activeGesture.Cancel();
			this.ExtendedItem.PropertyChanged -= OnPropertyChanged;
			this.Services.Selection.PrimarySelectionChanged -= OnPrimarySelectionChanged;
			base.OnRemove();
		}
		
		void OnPrimarySelectionChanged(object sender, EventArgs e)
		{
			bool isPrimarySelection = this.Services.Selection.PrimarySelection == this.ExtendedItem;
			foreach (DesignerThumb g in adornerPanel.Children) {
				g.IsPrimarySelection = isPrimarySelection;
			}
		}
		
		void UpdateAdornerVisibility()
		{
			FrameworkElement fe = this.ExtendedItem.View as FrameworkElement;
			foreach (DesignerThumb r in _designerThumbs) {
				bool isVisible = resizeBehavior != null && resizeBehavior.CanPlace(extendedItemArray, PlacementType.Resize, r.Alignment);
				r.Visibility = isVisible ? Visibility.Visible : Visibility.Hidden;
			}
		}
		
		void ShowSizeAndHideHandles()
		{
			SizeDisplayExtension sizeDisplay=null;
			MarginHandleExtension marginDisplay=null;
			foreach(var extension in ExtendedItem.Extensions) {
				if (extension is SizeDisplayExtension)
					sizeDisplay = extension as SizeDisplayExtension;
				if (extension is MarginHandleExtension)
					marginDisplay = extension as MarginHandleExtension;
			}

			if(sizeDisplay!=null) {
				sizeDisplay.HeightDisplay.Visibility = Visibility.Visible;
				sizeDisplay.WidthDisplay.Visibility = Visibility.Visible;
			}
			
			if(marginDisplay!=null)
				marginDisplay.HideHandles();
		}

		void HideSizeAndShowHandles()
		{
			SizeDisplayExtension sizeDisplay = null;
			MarginHandleExtension marginDisplay=null;
			foreach (var extension in ExtendedItem.Extensions){
				if (extension is SizeDisplayExtension)
					sizeDisplay = extension as SizeDisplayExtension;
				if (extension is MarginHandleExtension)
					marginDisplay = extension as MarginHandleExtension;
			}

			if (sizeDisplay != null) {
				sizeDisplay.HeightDisplay.Visibility = Visibility.Hidden;
				sizeDisplay.WidthDisplay.Visibility = Visibility.Hidden;
			}
			if (marginDisplay !=null ) {
				marginDisplay.ShowHandles();
			}
		}
	}
}
