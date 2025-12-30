using Hexa.NET.ImGui;
using Hexa.NET.OpenGL;
//using OGNES.UI.Enums;
//using OGNES.UI.Formats;
//using OGNES.UI.Formats.Decoders;
//using OGNES.UI.Formats.Models;
using System.Numerics;

namespace OGNES.UI
{
	/// <summary>
	/// Specialized viewer for IMG archive files with grid view, list view, and animation playback.
	/// </summary>
	public class ImgArchiveViewer
	{
		private readonly GL? _gl;
		//private readonly FormatManager? _formatManager;
		
		//private ImgFile? _imgFile;
		//private AniMagicArchiveType _archiveType = AniMagicArchiveType.IMMeen;
		//private DecodedPalette? _palette;
		
		// View mode
		private enum ViewMode
		{
			Grid,
			List,
			Single,
			Animation
		}
		private ViewMode _currentMode = ViewMode.Grid;
		
		// Image data
		private class ImageEntry
		{
			//public ImgFileImageType Type { get; set; }
			public int Index { get; set; }
			public int GlobalIndex { get; set; }
			public uint TextureId { get; set; }
			public int Width { get; set; }
			public int Height { get; set; }
			public byte[]? RawData { get; set; }
			//public string Name => $"{Type}_{Index:D4}";
		}
		
		private List<ImageEntry> _images = new();
		private int _selectedIndex = 0;
		
		// Grid view settings
		private int _thumbnailSize = 64;
		private int _gridColumns = 8;
		
		// Animation settings
		private bool _isPlaying = false;
		private float _fps = 10.0f;
		private bool _loop = true;
		private float _animationTimer = 0f;
		private int _animationFrame = 0;
		
		// Filter by type
		private bool _showImages = true;
		private bool _showSwitch = true;
		private bool _showMasked = true;

		//public ImgArchiveViewer(GL? gl = null, FormatManager? formatManager = null)
		//{
		//	_gl = gl;
		//	_formatManager = formatManager;
		//}

		/// <summary>
		/// Load an IMG file for viewing.
		/// </summary>
		//public void LoadImgFile(byte[] imgData, string fileName, AniMagicArchiveType archiveType, DecodedPalette palette)
		//{
		//	_imgFile = new ImgFile(imgData, fileName, archiveType);
		//	_archiveType = archiveType;
		//	_palette = palette;
		//	_images.Clear();
		//	_selectedIndex = 0;

		//	// Load all images and create textures
		//	LoadAllImages();
		//}

		//private void LoadAllImages()
		//{
		//	if (_imgFile == null || _palette == null || _gl == null) return;

		//	int globalIndex = 0;

		//	// Load regular images
		//	for (int i = 0; i < _imgFile.ImageCount; i++)
		//	{
		//		LoadImage(ImgFileImageType.Image, i, globalIndex++);
		//	}

		//	// Load switch images
		//	for (int i = 0; i < _imgFile.SwitchImageCount; i++)
		//	{
		//		LoadImage(ImgFileImageType.Switch, i, globalIndex++);
		//	}

		//	// Load masked images
		//	for (int i = 0; i < _imgFile.MaskedImageCount; i++)
		//	{
		//		LoadMaskedImage(ImgFileImageType.Masked, i, globalIndex++);
		//	}
		//}

  //  private unsafe void LoadImage(ImgFileImageType type, int index, int globalIndex)
  //  {

  //    if (_imgFile == null || _palette == null || _gl == null) return;

  //    try
  //    {
  //      var rawData = _imgFile.GetImageData(index, type);
  //      var decoder = new Indexed8ImageDecoder(64,64);
  //      var decodedImage = decoder.Decode(rawData, _palette, useTransparency: true);
  //      // Create OpenGL texture
  //      uint textureId = 0;
  //      _gl.GenTextures(1, &textureId);
  //      _gl.BindTexture(GLTextureTarget.Texture2D, textureId);

  //      _gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.MinFilter, (int)GLTextureMinFilter.Nearest);
  //      _gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.MagFilter, (int)GLTextureMagFilter.Nearest);

  //      fixed (byte* ptr = decodedImage.PixelData)
  //      {
  //        _gl.TexImage2D(
  //          GLTextureTarget.Texture2D,
  //          0,
  //          GLInternalFormat.Rgba,
  //          decodedImage.Width,
  //          decodedImage.Height,
  //          0,
  //          GLPixelFormat.Rgba,
  //          GLPixelType.UnsignedByte,
  //          (nint)ptr
  //        );
  //      }

  //      _images.Add(new ImageEntry
  //      {
  //        Type = type,
  //        Index = index,
  //        GlobalIndex = globalIndex,
  //        TextureId = textureId,
  //        Width = decodedImage.Width,
  //        Height = decodedImage.Height,
  //        RawData = rawData
  //      });
  //    }
  //    catch (Exception ex)
  //    {
  //      Console.WriteLine($"Error loading image {type} #{index}: {ex.Message}");
  //    }
  //  }

		//private unsafe void LoadMaskedImage(ImgFileImageType type, int index, int globalIndex)
		//{
		//	if (_imgFile == null || _palette == null || _gl == null) return;

		//	try
		//	{
		//		var rawData = _imgFile.GetImageData(index, type);
		//		var decoder = new ImgImageDecoder(_archiveType);
		//		var decodedImage = decoder.Decode(rawData, _palette, useTransparency: true);

		//		// Create OpenGL texture
		//		uint textureId = 0;
		//		_gl.GenTextures(1, &textureId);
		//		_gl.BindTexture(GLTextureTarget.Texture2D, textureId);
				
		//		_gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.MinFilter, (int)GLTextureMinFilter.Nearest);
		//		_gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.MagFilter, (int)GLTextureMagFilter.Nearest);

		//		fixed (byte* ptr = decodedImage.PixelData)
		//		{
		//			_gl.TexImage2D(
		//				GLTextureTarget.Texture2D,
		//				0,
		//				GLInternalFormat.Rgba,
		//				decodedImage.Width,
		//				decodedImage.Height,
		//				0,
		//				GLPixelFormat.Rgba,
		//				GLPixelType.UnsignedByte,
		//				(nint)ptr
		//			);
		//		}

		//		_images.Add(new ImageEntry
		//		{
		//			Type = type,
		//			Index = index,
		//			GlobalIndex = globalIndex,
		//			TextureId = textureId,
		//			Width = decodedImage.Width,
		//			Height = decodedImage.Height,
		//			RawData = rawData
		//		});
		//	}
		//	catch (Exception ex)
		//	{
		//		Console.WriteLine($"Error loading image {type} #{index}: {ex.Message}");
		//	}
		//}

		/// <summary>
		/// Render the IMG archive viewer UI.
		/// </summary>
		public void Render()
		{
			//if (_imgFile == null)
			//{
			//	ImGui.Text("No IMG file loaded");
			//	return;
			//}

			RenderToolbar();
			ImGui.Separator();

			// Render based on current mode
			switch (_currentMode)
			{
				case ViewMode.Grid:
					RenderGridView();
					break;
				case ViewMode.List:
					RenderListView();
					break;
				case ViewMode.Single:
					RenderSingleView();
					break;
				case ViewMode.Animation:
					RenderAnimationView();
					break;
			}
		}

		private void RenderToolbar()
		{
			// View mode selector
			if (ImGui.RadioButton("Grid", _currentMode == ViewMode.Grid))
				_currentMode = ViewMode.Grid;
			ImGui.SameLine();
			if (ImGui.RadioButton("List", _currentMode == ViewMode.List))
				_currentMode = ViewMode.List;
			ImGui.SameLine();
			if (ImGui.RadioButton("Single", _currentMode == ViewMode.Single))
				_currentMode = ViewMode.Single;
			ImGui.SameLine();
			if (ImGui.RadioButton("Animation", _currentMode == ViewMode.Animation))
				_currentMode = ViewMode.Animation;

			ImGui.SameLine();
			ImGui.Spacing();
			ImGui.SameLine();

			// Type filters
			ImGui.Checkbox("Images", ref _showImages);
			ImGui.SameLine();
			ImGui.Checkbox("Switch", ref _showSwitch);
			ImGui.SameLine();
			ImGui.Checkbox("Masked", ref _showMasked);

			// Archive info
			ImGui.SameLine();
			ImGui.Spacing();
			ImGui.SameLine();
			ImGui.Text($"| Total: {GetFilteredImages().Count} images");
		}

	private unsafe void RenderGridView()
	{
		ImGui.SliderInt("Thumbnail Size", ref _thumbnailSize, 32, 256);

		var filteredImages = GetFilteredImages();
		if (filteredImages.Count == 0)
		{
			ImGui.Text("No images to display (check filters)");
			return;
		}

		if (ImGui.BeginChild("GridView", new Vector2(0, 0), ImGuiChildFlags.None))
		{
			var availableWidth = ImGui.GetContentRegionAvail().X;
			// Account for padding and spacing between items
			float itemWidth = _thumbnailSize + 8; // thumbnail + some padding
			_gridColumns = Math.Max(1, (int)(availableWidth / itemWidth));

			int column = 0;
			float startX = ImGui.GetCursorPosX();

			foreach (var image in filteredImages)
			{
				// Set cursor position for proper grid alignment
				if (column > 0)
				{
					ImGui.SameLine();
					ImGui.SetCursorPosX(startX + (column * itemWidth));
				}

				ImGui.BeginGroup();
				ImGui.PushID(image.GlobalIndex);
				
				// Thumbnail
				var selected = image.GlobalIndex == _selectedIndex;
				if (selected)
				{
					var cursorPos = ImGui.GetCursorScreenPos();
					var drawList = ImGui.GetWindowDrawList();
					drawList.AddRect(
						cursorPos - new Vector2(2, 2),
						cursorPos + new Vector2(_thumbnailSize + 2, _thumbnailSize + 22),
						ImGui.GetColorU32(new Vector4(1, 1, 0, 1)),
						0, 0, 2);
				}
				
				// Thumbnail image
				ImGui.Image(new ImTextureRef(null, image.TextureId), new Vector2(_thumbnailSize, _thumbnailSize));
				
				// Click to select
				if (ImGui.IsItemClicked())
				{
					_selectedIndex = image.GlobalIndex;
					_currentMode = ViewMode.Single;
				}

				//// Label
				//var labelColor = image.Type switch
				//{
				//	ImgFileImageType.Image => new Vector4(0.8f, 0.8f, 1.0f, 1.0f),
				//	ImgFileImageType.Switch => new Vector4(1.0f, 0.8f, 0.5f, 1.0f),
				//	ImgFileImageType.Masked => new Vector4(0.5f, 1.0f, 0.8f, 1.0f),
				//	_ => new Vector4(1, 1, 1, 1)
				//};
				//ImGui.TextColored(labelColor, image.Name);

				ImGui.PopID();
				ImGui.EndGroup();

				column++;
				if (column >= _gridColumns)
				{
					column = 0;
					ImGui.NewLine();
				}
				}
			}
			ImGui.EndChild();
		}

		private unsafe void RenderListView()
		{
			var filteredImages = GetFilteredImages();
			if (filteredImages.Count == 0)
			{
				ImGui.Text("No images to display (check filters)");
				return;
			}

			if (ImGui.BeginTable("ImageList", 5,
				ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable))
			{
				ImGui.TableSetupColumn("Preview", ImGuiTableColumnFlags.WidthFixed, 64);
				ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 100);
				ImGui.TableSetupColumn("Index", ImGuiTableColumnFlags.WidthFixed, 60);
				ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed, 100);
				ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
				ImGui.TableHeadersRow();

				foreach (var image in filteredImages)
				{
					ImGui.TableNextRow();
					ImGui.TableNextColumn();

					// Preview
					//ImGui.PushID($"{image.Name}_preview");
					ImGui.Image(new ImTextureRef(null, image.TextureId), new Vector2(48, 48));
					if (ImGui.IsItemClicked())
					{
						_selectedIndex = image.GlobalIndex;
						_currentMode = ViewMode.Single;
					}
					ImGui.PopID();

					ImGui.TableNextColumn();
					//var typeColor = image.Type switch
					//{
					//	ImgFileImageType.Image => new Vector4(0.8f, 0.8f, 1.0f, 1.0f),
					//	ImgFileImageType.Switch => new Vector4(1.0f, 0.8f, 0.5f, 1.0f),
					//	ImgFileImageType.Masked => new Vector4(0.5f, 1.0f, 0.8f, 1.0f),
					//	_ => new Vector4(1, 1, 1, 1)
					//};
					//ImGui.TextColored(typeColor, image.Type.ToString());

					ImGui.TableNextColumn();
					ImGui.Text(image.Index.ToString());

					ImGui.TableNextColumn();
					ImGui.Text($"{image.Width}x{image.Height}");

					ImGui.TableNextColumn();
					//ImGui.Text(image.Name);
				}

				ImGui.EndTable();
			}
		}

		private unsafe void RenderSingleView()
		{
			var filteredImages = GetFilteredImages();
			if (filteredImages.Count == 0)
			{
				ImGui.Text("No images to display (check filters)");
				return;
			}

			if (_selectedIndex < 0 || _selectedIndex >= _images.Count)
				_selectedIndex = 0;

			var image = _images[_selectedIndex];

			// Navigation
			if (ImGui.Button("< Previous"))
			{
				_selectedIndex = (_selectedIndex - 1 + _images.Count) % _images.Count;
			}
			ImGui.SameLine();
			if (ImGui.Button("Next >"))
			{
				_selectedIndex = (_selectedIndex + 1) % _images.Count;
			}
			ImGui.SameLine();
			ImGui.Text($"| Image {_selectedIndex + 1} of {_images.Count}");

			ImGui.Separator();

			// Image info
			//ImGui.Text($"Name: {image.Name}");
			//ImGui.Text($"Type: {image.Type}");
			ImGui.Text($"Size: {image.Width}x{image.Height}");
			ImGui.Text($"Raw Data: {image.RawData?.Length ?? 0} bytes");

			ImGui.Separator();

			// Display image (centered and scaled)
			var availableSize = ImGui.GetContentRegionAvail();
			var imageSize = new Vector2(image.Width, image.Height);
			
			// Calculate scale to fit
			float scale = Math.Min(
				availableSize.X / imageSize.X,
				availableSize.Y / imageSize.Y
			);
			scale = Math.Min(scale, 8.0f); // Max 8x zoom

			var displaySize = imageSize * scale;
			var offset = (availableSize - displaySize) / 2;

			ImGui.SetCursorPos(ImGui.GetCursorPos() + offset);
			ImGui.Image(new ImTextureRef(null, image.TextureId), displaySize);
		}

		private unsafe void RenderAnimationView()
		{
			var filteredImages = GetFilteredImages();
			if (filteredImages.Count == 0)
			{
				ImGui.Text("No images to display (check filters)");
				return;
			}

			// Animation controls
			if (_isPlaying)
			{
				if (ImGui.Button("|| Pause"))
					_isPlaying = false;
			}
			else
			{
				if (ImGui.Button("> Play"))
					_isPlaying = true;
			}

			ImGui.SameLine();
			if (ImGui.Button("[] Stop"))
			{
				_isPlaying = false;
				_animationFrame = 0;
			}

			ImGui.SameLine();
			ImGui.SetNextItemWidth(100);
			ImGui.SliderFloat("FPS", ref _fps, 1.0f, 60.0f, "%.1f");

			ImGui.SameLine();
			ImGui.Checkbox("Loop", ref _loop);

			ImGui.SameLine();
			ImGui.Text($"| Frame {_animationFrame + 1} / {filteredImages.Count}");

			// Update animation
			if (_isPlaying)
			{
				_animationTimer += ImGui.GetIO().DeltaTime;
				float frameTime = 1.0f / _fps;

				if (_animationTimer >= frameTime)
				{
					_animationTimer -= frameTime;
					_animationFrame++;

					if (_animationFrame >= filteredImages.Count)
					{
						if (_loop)
							_animationFrame = 0;
						else
						{
							_animationFrame = filteredImages.Count - 1;
							_isPlaying = false;
						}
					}
				}
			}

			// Ensure frame is in bounds
			if (_animationFrame < 0 || _animationFrame >= filteredImages.Count)
				_animationFrame = 0;

			ImGui.Separator();

			// Display current frame
			var image = filteredImages[_animationFrame];
			
			var availableSize = ImGui.GetContentRegionAvail();
			var imageSize = new Vector2(image.Width, image.Height);
			
			float scale = Math.Min(
				availableSize.X / imageSize.X,
				availableSize.Y / imageSize.Y
			);
			scale = Math.Min(scale, 8.0f);

			var displaySize = imageSize * scale;
			var offset = (availableSize - displaySize) / 2;

			ImGui.SetCursorPos(ImGui.GetCursorPos() + offset);
			ImGui.Image(new ImTextureRef(null, image.TextureId), displaySize);
		}

		private List<ImageEntry> GetFilteredImages()
		{
			return _images
				//.Where(img => 
				//(img.Type == ImgFileImageType.Image && _showImages) ||
				//(img.Type == ImgFileImageType.Switch && _showSwitch) ||
				//(img.Type == ImgFileImageType.Masked && _showMasked))
				.ToList();
		}

		/// <summary>
		/// Clean up OpenGL textures.
		/// </summary>
		public unsafe void Dispose()
		{
			if (_gl == null) return;

			foreach (var image in _images)
			{
				if (image.TextureId != 0)
				{
					uint textureId = image.TextureId;
					_gl.DeleteTextures(1, &textureId);
				}
			}

			_images.Clear();
		}
	}
}
