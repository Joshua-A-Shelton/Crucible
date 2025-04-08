# Mesh Chunk Format #
Here in lies the specification for mesh data for the Crucible Engine

    #1) Index size                          (8 bit boolean value indicating index buffer contents, false for unsigned 16 bit integers, true for 32 bit unsigned integers)
    #2) Size of compressed index data       (Little endian 32 bit signed integer)
    #3) Size of uncompressed index data     (Little endian 32 bit signed integer)
    #4) Index data stream                   (Sequence of LZ4 compressed bytes that represents index data, total length equivelent to #2, uncompressed size equivelent to #3)*1
    #5) Vertex attribute count              (8 bit unsigned integer)
    #6) Vertex attribute streams            (collection of #5(Vertex attribute count) number of vertex attribute streams)*2

*1 The Index data is layout out thusly (when uncompressed)

    If #1(Index size) == false:
        stream of bytes of #3(Size of uncompressed index data) length, containing unsigned 16 bit unsigned integers
    If #1(Index size) == true:
        stream of bytes of #3(Size of uncompressed index data) length, containing unsigned 32 bit unsigned integers

    Note that in either case, #3 refers to the size of the buffer in bytes, not the number of elements in the buffer

*2 The Vertex attribute streams are layed out thusly

    #1) Attribute type                      (Little Endian 16 bit flag indicating attribute type)*3
    #2) Size of compressed attribute data   (Little endian 32 bit signed integer)
    #3) Size of uncompressed attribute data (Little endian 32 bit signed integer)
    #4) Vertex attribute stream             (Sequence of LZ4 compressed bytes that represent vertex attribute data, total length equivelent to #2, uncompressed size equivelent to #3)*4

Attribute streams do not have to be in any specific order in respect to their type, but there should not be multiple streams with the same Attribute type

*3 The attribute type flags are listed as follows

    0x0000000000000001 = 3D Vertex Position data
    0x0000000000000010 = 2D Vertex Position data
    0x0000000000000100 = Vertex Normals
    0x0000000000001000 = UV Coordinates
    0x0000000000010000 = Color
    0x0000000000100000 = Bone Weights
    0x0000000001000000 = RESERVED FOR FUTURE USE
    0x0000000010000000 = RESERVED FOR FUTURE USE
    0x0000000100000000 = RESERVED FOR FUTURE USE
    0x0000001000000000 = RESERVED FOR FUTURE USE
    0x0000010000000000 = RESERVED FOR FUTURE USE
    0x0000100000000000 = RESERVED FOR FUTURE USE
    0x0001000000000000 = RESERVED FOR FUTURE USE
    0x0010000000000000 = RESERVED FOR FUTURE USE
    0x0100000000000000 = RESERVED FOR FUTURE USE
    0x1000000000000000 = RESERVED FOR FUTURE USE

*4 The attribute data stored in the stream is expected to be layed out (when uncompressed) as an array of the attribute described as follows 

    3D Vertex Position data         (3 little endian 32 bit floats, representing X,Y,Z coordinates in that order, with positive X being right, positive Y being up, and positive Z being forward)
    2D Vertex Position data         (2 little endian 32 bit floats, representing X and Y coordinates in that order, with positive X being right, and positive Y being up)
    Vertex Normals                  (3 little endian 32 bit floats, representing X,Y,Z direction of a normal vector in that order, with positive X being right, positive Y being up, and positive Z being forward)
    UV Coordinates                  (2 little endian 32 bit floats, representing the U and V coordinates of an image, (0,0) being top left of an image, and (1,1) being the bottom right)
    Color                           (4 bytes representing color, with each byte being a channel, Red, Green, Blue and Alpha in that order)
    Bone Weights                    (4 bone weights)*5

*5 Bone weights are layed out as follows

    Bone ID                         (Little endian 16 bit unsigned integer)
    Bone Influence                  (little endian 32 bit float)

The total of all 4 bone influences in the bone weight should add up to 1.0